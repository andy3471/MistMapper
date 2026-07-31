using MistMapper.Host.Drivers;
using MistMapper.Host.Logging;
using MistMapper.Host.Mapping;
using MistMapper.Host.Steam;
using MistMapper.Host.Viiper;
using MistMapper.Shared;

namespace MistMapper.Host.Services;

/// <summary>
/// Orchestrates multi-pad driver → map → sinks, Steam pause, session lock, per-game profiles, VIIPER health.
/// </summary>
public sealed partial class BridgeService : IDisposable
{
    readonly ProfileService _profiles;
    readonly ISteamState _steam;
    readonly ISessionState _session;
    readonly IForegroundState _foreground;
    readonly IGameBarState _gameBar;
    readonly DriverRegistry _drivers;
    readonly IViiperHealth _viiperHealth;
    readonly Func<IViiperClient> _viiperFactory;
    readonly IMouseSink _mouse;
    readonly IKeyboardSink _keyboard;
    readonly ControllerProfile _gameBarGamepadProfile = OfficialLayouts.CreateGamepad();
    readonly object _gate = new();
    readonly List<BridgeSlot> _slots = [];

    CancellationTokenSource? _cts;
    Task? _loop;
    BridgeStatus _status = new();
    DateTime _lastKeepalive = DateTime.MinValue;
    DateTime _lastViiperProbe = DateTime.MinValue;
    DateTime _lastDeviceSync = DateTime.MinValue;
    DependencyStatus _viiperDep = new()
    {
        Ok = false,
        Detail = "Not checked yet"
    };
    string? _lastError;
    ActiveProfileSource _profileSource = ActiveProfileSource.Default;
    ControllerProfile? _resolvedProfile;
    bool _notifiedViiperDown;
    string _lastForegroundExe = "";
    bool _manualProfileLock;
    bool _gameBarWidgetOpen;
    DateTime _lastBridgePublish = DateTime.MinValue;
    string _selectedDeviceKey = "";

    public event Action<BridgeStatus>? StatusChanged;

    public BridgeService(
        ProfileService profiles,
        ISteamState steam,
        ISessionState session,
        IForegroundState? foreground = null,
        IGameBarState? gameBar = null,
        DriverRegistry? drivers = null,
        MappingEngine? mapper = null,
        IMouseSink? mouse = null,
        IKeyboardSink? keyboard = null,
        IViiperHealth? viiperHealth = null,
        Func<IViiperClient>? viiperFactory = null)
    {
        _profiles = profiles;
        _steam = steam;
        _session = session;
        _foreground = foreground ?? new ForegroundWatcher();
        _gameBar = gameBar ?? new GameBarWatcher();
        _drivers = drivers ?? new DriverRegistry();
        _mouse = mouse ?? Win32MouseSink.Instance;
        _keyboard = keyboard ?? Win32KeyboardSink.Instance;
        // Shared mapper ctor arg kept for API compat; each slot gets its own engine.
        _ = mapper;
        _viiperHealth = viiperHealth ?? new ViiperHealth();
        _viiperFactory = viiperFactory ?? (() => new ViiperXbox360Client());
        _viiperDep.Id = _viiperHealth.DependencyId;
        _viiperDep.DisplayName = _viiperHealth.DisplayName;
        _gameBarGamepadProfile.Name = "Gamepad (Game Bar override)";
        _steam.Changed += _ => PublishStatus();
        _gameBar.Changed += _ => PublishStatus();
        _session.Changed += locked =>
        {
            if (locked) EnterLizardForLock();
            else PublishStatus();
        };
        _profiles.Changed += () =>
        {
            RefreshSlotOverridesFromStore();
            if (!_manualProfileLock)
                ResolveProfile();
            else
            {
                _resolvedProfile = _profiles.ActiveProfile;
                _profileSource = ActiveProfileSource.Manual;
            }
            PublishStatus();
        };
        _foreground.Changed += () =>
        {
            _manualProfileLock = false;
            ResolveProfile();
            PublishStatus();
        };
        ResolveProfile();
    }

    public DriverRegistry Drivers => _drivers;

    /// <summary>Capabilities for the selected (or primary) connected pad, model-aware.</summary>
    public DriverCapabilities GetActiveCapabilities()
    {
        lock (_gate)
        {
            var selected = FindSlotUnlocked(_selectedDeviceKey)
                ?? _slots.Where(s => s.Driver.IsConnected).OrderBy(s => s.Order).FirstOrDefault();
            if (selected?.Driver is not null)
                return selected.Driver.Capabilities;
        }

        var status = Status;
        return _drivers.GetCapabilities(status.ActiveDriverId, status.ControllerModel);
    }

    public void SetGameBarWidgetOpen(bool open)
    {
        bool changed;
        lock (_gate)
        {
            changed = _gameBarWidgetOpen != open;
            _gameBarWidgetOpen = open;
        }
        if (changed)
            PublishStatus();
    }

    public BridgeStatus Status
    {
        get { lock (_gate) return CloneStatus(_status); }
    }

    public void Start()
    {
        if (_loop is not null) return;
        AppLog.Current.Info("BridgeService starting");
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => RunLoopAsync(_cts.Token));
        PublishStatus(BridgeRunState.WaitingForController, "Starting…");
    }

    public void Stop()
    {
        AppLog.Current.Info("BridgeService stopping");
        _cts?.Cancel();
        try { _loop?.Wait(2000); } catch { /* ignore */ }
        TearDownAllSlots(restoreExclusive: true);
        PublishStatus(BridgeRunState.Stopped, "Stopped");
        AppLog.Current.Info("BridgeService stopped");
    }

    public void SetEnabled(bool enabled)
    {
        _profiles.BridgeEnabled = enabled;
        PublishStatus();
    }

    public void SetSelectedController(string deviceKey)
    {
        lock (_gate)
            _selectedDeviceKey = deviceKey ?? "";
        PublishStatus();
    }

    public async Task IdentifyControllerAsync(string deviceKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(deviceKey))
            throw new ArgumentException("deviceKey required");

        BridgeSlot? slot;
        lock (_gate)
            slot = FindSlotUnlocked(deviceKey);

        if (slot is null || !slot.Driver.IsConnected)
            throw new InvalidOperationException("Controller is not connected.");

        // Pause VIIPER rumble forwarding so a zeroed game rumble doesn't cancel the pulse.
        slot.UnhookRumble();
        try
        {
            var ok = await slot.Driver.IdentifyAsync(ct);
            if (!ok)
                throw new InvalidOperationException("Rumble failed — this interface may not support haptics.");
        }
        finally
        {
            if (slot.Viiper?.IsConnected == true)
                slot.HookRumble();
        }
    }

    public void RenameController(string deviceKey, string? displayName)
    {
        if (string.IsNullOrWhiteSpace(deviceKey))
            throw new ArgumentException("deviceKey required");

        _profiles.SetControllerSlotDisplayName(deviceKey, displayName);
        lock (_gate)
        {
            var slot = FindSlotUnlocked(deviceKey);
            if (slot is not null)
            {
                slot.DisplayName = string.IsNullOrWhiteSpace(displayName)
                    ? DisplayNameForModel(slot.Model)
                    : displayName.Trim();
            }
        }
        PublishStatus();
    }

    public void SetControllerRumbleEnabled(string deviceKey, bool rumbleEnabled)
    {
        if (string.IsNullOrWhiteSpace(deviceKey))
            throw new ArgumentException("deviceKey required");

        _profiles.SetControllerRumbleEnabled(deviceKey, rumbleEnabled);
        lock (_gate)
        {
            var slot = FindSlotUnlocked(deviceKey);
            if (slot is not null)
            {
                slot.RumbleEnabled = rumbleEnabled;
                if (!rumbleEnabled)
                {
                    try { slot.Driver.SetRumble(0, 0); } catch { /* ignore */ }
                }
            }
        }
        PublishStatus();
    }

    async Task RunLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RefreshViiperProbeAsync(ct);

                if (!_profiles.BridgeEnabled)
                {
                    TearDownAllSlots(restoreExclusive: true);
                    PublishStatus(BridgeRunState.Stopped, "Bridge disabled");
                    await Task.Delay(500, ct);
                    continue;
                }

                if (!_viiperDep.Ok)
                {
                    PublishStatus(BridgeRunState.Error, "VIIPER missing — trying to start local install…");
                    var ensured = await _viiperHealth.EnsureRunningAsync(ct);
                    _viiperDep.Ok = ensured.Ok;
                    _viiperDep.Detail = ensured.Detail;
                    if (!ensured.Ok)
                    {
                        TearDownVirtualOnly();
                        ReleaseAllMappers();
                        PublishStatus(BridgeRunState.Error, _viiperDep.Detail);
                        await Task.Delay(2000, ct);
                        continue;
                    }
                }

                if (_session.IsLocked)
                {
                    TearDownAllSlots(restoreExclusive: true);
                    PublishStatus(BridgeRunState.PausedLocked, "Session locked — lizard mode");
                    await Task.Delay(400, ct);
                    continue;
                }

                if (_profiles.AutoPauseWhenSteamRunning && _steam.IsSteamRunning)
                {
                    TearDownAllSlots(restoreExclusive: true);
                    PublishStatus(BridgeRunState.PausedSteam, "Steam running — bridge paused");
                    await Task.Delay(1000, ct);
                    continue;
                }

                if ((DateTime.UtcNow - _lastDeviceSync).TotalMilliseconds >= 1000
                    || _slots.Count == 0
                    || _slots.Any(s => !s.Driver.IsConnected))
                {
                    await SyncSlotsAsync(ct);
                    _lastDeviceSync = DateTime.UtcNow;
                }

                if (_slots.Count == 0 || _slots.All(s => !s.Driver.IsConnected))
                {
                    PublishStatus(BridgeRunState.WaitingForController, "Waiting for controller…");
                    await Task.Delay(1000, ct);
                    continue;
                }

                var primaryKey = PrimaryKeyboardMouseDeviceKey();
                var anyBridging = false;
                var anyViiper = false;

                foreach (var slot in _slots.OrderBy(s => s.Order).ToList())
                {
                    if (!slot.Enabled || !slot.Driver.IsConnected)
                        continue;

                    if (slot.Viiper is null || !slot.Viiper.IsConnected)
                    {
                        if (!await EnsureViiperForSlotAsync(slot, ct))
                            continue;
                    }

                    anyViiper = true;
                    if (!slot.Driver.TryRead(out var frame))
                    {
                        if (!slot.Driver.IsConnected)
                            continue;
                        continue;
                    }

                    var (profile, source, _) = ResolveProfileForSlot(slot.DeviceKey, slot.ProfileId);
                    slot.ResolvedProfile = profile;
                    slot.ProfileSource = source;

                    var gameBarOpen = IsGameBarOverrideActive();
                    var mapProfile = profile;
                    if (gameBarOpen)
                    {
                        // Stock Xbox buttons for overlay navigation, but keep the
                        // user's gyro / pad modes so aim still works while Win+G is open.
                        ApplyGameBarOverrideSurfaces(profile, _gameBarGamepadProfile);
                        mapProfile = _gameBarGamepadProfile;
                    }
                    var allowKbMouse = string.Equals(slot.DeviceKey, primaryKey, StringComparison.OrdinalIgnoreCase);
                    var xbox = slot.Mapper.Map(frame, mapProfile, allowKbMouse);
                    slot.Viiper!.SendInput(xbox);

                    if (allowKbMouse && slot.Mapper.TryConsumeMouseDelta(out int dx, out int dy))
                        _mouse.Move(dx, dy);

                    if (allowKbMouse && slot.Mapper.TryConsumeMouseWheel(out int wheel))
                        _mouse.Scroll(wheel);

                    while (slot.Mapper.TryConsumeMouseHaptic(out bool rightPad, out byte hapIntensity))
                        slot.Driver.PulseMouseHaptic(rightPad, hapIntensity);

                    slot.Pressed = frame.PressedDigitalIds().ToList();
                    anyBridging = true;
                }

                if ((DateTime.UtcNow - _lastKeepalive).TotalMilliseconds > 800)
                {
                    foreach (var slot in _slots)
                    {
                        if (slot.Driver.IsConnected)
                            slot.Driver.KeepAlive();
                    }
                    _lastKeepalive = DateTime.UtcNow;
                }

                if (anyBridging && (DateTime.UtcNow - _lastBridgePublish).TotalMilliseconds >= 100)
                {
                    var primary = PrimarySlotUnlocked();
                    var profile = primary?.ResolvedProfile ?? _resolvedProfile ?? _profiles.ActiveProfile;
                    var src = (primary?.ProfileSource ?? _profileSource) == ActiveProfileSource.GameRule
                        ? "game"
                        : "manual";
                    var n = _slots.Count(s => s.Driver.IsConnected);
                    PublishStatus(BridgeRunState.Bridging, $"Bridging {n} pad(s) — {profile.Name} ({src})");
                    _lastBridgePublish = DateTime.UtcNow;
                }
                else if (!anyBridging && !anyViiper)
                {
                    PublishStatus(BridgeRunState.WaitingForController, "Waiting for controller…");
                }

                _ = anyViiper;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                AppLog.Current.Error("Bridge loop error", ex);
                PublishStatus(BridgeRunState.Error, ex.Message);
                await Task.Delay(1000, ct);
            }
        }

        TearDownAllSlots(restoreExclusive: true);
    }

    public bool ConsumeViiperDownNotification()
    {
        if (_viiperDep.Ok || _notifiedViiperDown) return false;
        _notifiedViiperDown = true;
        return true;
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
        _foreground.Dispose();
        _gameBar.Dispose();
    }
}
