using SteamControllerBridge.Host.Drivers;
using SteamControllerBridge.Host.Mapping;
using SteamControllerBridge.Host.Viiper;
using SteamControllerBridge.Shared;

namespace SteamControllerBridge.Host.Services;

/// <summary>
/// Orchestrates driver → map → sinks, Steam pause, session lock, per-game profiles, VIIPER health.
/// While Xbox Game Bar is open, runtime output uses a fixed OfficialLayouts.Gamepad map;
/// the active profile (and Game Bar widget UI) is unchanged.
/// </summary>
public sealed class BridgeService : IDisposable
{
    readonly ProfileService _profiles;
    readonly SteamWatcher _steam;
    readonly SessionWatcher _session;
    readonly ForegroundWatcher _foreground;
    readonly GameBarWatcher _gameBar;
    readonly DriverRegistry _drivers;
    readonly MappingEngine _mapper = new();
    readonly ControllerProfile _gameBarGamepadProfile = OfficialLayouts.CreateGamepad();
    readonly object _gate = new();

    IControllerDriver? _activeDriver;
    ViiperXbox360Client? _viiper;
    CancellationTokenSource? _cts;
    Task? _loop;
    BridgeStatus _status = new();
    DateTime _lastKeepalive = DateTime.MinValue;
    DateTime _lastViiperProbe = DateTime.MinValue;
    DependencyStatus _viiperDep = new()
    {
        Id = ViiperHealth.DependencyId,
        DisplayName = ViiperHealth.DisplayName,
        Ok = false,
        Detail = "Not checked yet"
    };
    string? _lastError;
    List<string> _pressed = [];
    ActiveProfileSource _profileSource = ActiveProfileSource.Default;
    ControllerProfile? _resolvedProfile;
    bool _notifiedViiperDown;
    string _lastForegroundExe = "";
    bool _manualProfileLock;
    bool _gameBarWidgetOpen;
    DateTime _lastBridgePublish = DateTime.MinValue;

    public event Action<BridgeStatus>? StatusChanged;

    public BridgeService(
        ProfileService profiles,
        SteamWatcher steam,
        SessionWatcher session,
        ForegroundWatcher? foreground = null,
        GameBarWatcher? gameBar = null,
        DriverRegistry? drivers = null)
    {
        _profiles = profiles;
        _steam = steam;
        _session = session;
        _foreground = foreground ?? new ForegroundWatcher();
        _gameBar = gameBar ?? new GameBarWatcher();
        _drivers = drivers ?? new DriverRegistry();
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
            // Profile list / remap / explicit active change from UI.
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

    /// <summary>
    /// Called by Game Bar file IPC when the widget heartbeat is fresh.
    /// While open, runtime mapping uses stock Gamepad; the widget UI still shows the saved profile.
    /// </summary>
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
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => RunLoopAsync(_cts.Token));
        PublishStatus(BridgeRunState.WaitingForController, "Starting…");
    }

    public void Stop()
    {
        _cts?.Cancel();
        try { _loop?.Wait(2000); } catch { /* ignore */ }
        TearDownBridge(restoreExclusive: true);
        _mapper.ReleaseAllInjected();
        PublishStatus(BridgeRunState.Stopped, "Stopped");
    }

    public void SetEnabled(bool enabled)
    {
        _profiles.BridgeEnabled = enabled;
        PublishStatus();
    }

    /// <summary>UI-driven profile pick; sticky until the foreground process changes.</summary>
    public void SetActiveProfileManual(string profileId)
    {
        _manualProfileLock = true;
        _profiles.SetActiveProfile(profileId);
        _resolvedProfile = _profiles.ActiveProfile;
        _profileSource = ActiveProfileSource.Manual;
        PublishStatus();
    }

    public void BindActiveProfileToCurrentGame()
    {
        var exe = _foreground.ExeName;
        if (string.IsNullOrWhiteSpace(exe))
            throw new InvalidOperationException("No foreground game detected yet.");
        var profile = _resolvedProfile ?? _profiles.ActiveProfile;
        _profiles.BindToGame(profile.Id, exe, null, Path.GetFileNameWithoutExtension(exe));
        _manualProfileLock = false;
        ResolveProfile();
        PublishStatus();
    }

    void ResolveProfile()
    {
        var exe = _foreground.ExeName;
        if (!string.Equals(exe, _lastForegroundExe, StringComparison.OrdinalIgnoreCase))
        {
            _lastForegroundExe = exe;
            _manualProfileLock = false;
        }

        if (_manualProfileLock)
        {
            _resolvedProfile = _profiles.ActiveProfile;
            _profileSource = ActiveProfileSource.Manual;
            return;
        }

        var (profile, source) = _profiles.ResolveForGame(exe, _foreground.Path);
        _resolvedProfile = profile;
        _profileSource = source;
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
                    TearDownBridge(restoreExclusive: true);
                    _mapper.ReleaseAllInjected();
                    PublishStatus(BridgeRunState.Stopped, "Bridge disabled");
                    await Task.Delay(500, ct);
                    continue;
                }

                if (!_viiperDep.Ok)
                {
                    PublishStatus(BridgeRunState.Error, "VIIPER missing — trying to start local install…");
                    var ensured = await ViiperHealth.EnsureRunningAsync(ct);
                    _viiperDep.Ok = ensured.Ok;
                    _viiperDep.Detail = ensured.Detail;
                    if (!ensured.Ok)
                    {
                        TearDownVirtualOnly();
                        _mapper.ReleaseAllInjected();
                        PublishStatus(BridgeRunState.Error, _viiperDep.Detail);
                        await Task.Delay(2000, ct);
                        continue;
                    }
                }

                if (_session.IsLocked)
                {
                    TearDownBridge(restoreExclusive: true);
                    _mapper.ReleaseAllInjected();
                    PublishStatus(BridgeRunState.PausedLocked, "Session locked — lizard mode");
                    await Task.Delay(400, ct);
                    continue;
                }

                if (_profiles.AutoPauseWhenSteamRunning && _steam.IsSteamRunning)
                {
                    TearDownBridge(restoreExclusive: true);
                    _mapper.ReleaseAllInjected();
                    PublishStatus(BridgeRunState.PausedSteam, "Steam running — bridge paused");
                    await Task.Delay(1000, ct);
                    continue;
                }

                if (_activeDriver is null || !_activeDriver.IsConnected)
                {
                    TearDownVirtualOnly();
                    _activeDriver = _drivers.TryOpenAny();
                    if (_activeDriver is null)
                    {
                        PublishStatus(BridgeRunState.WaitingForController, "Waiting for controller…");
                        await Task.Delay(1000, ct);
                        continue;
                    }
                }

                if (_viiper is null || !_viiper.IsConnected)
                {
                    PublishStatus(BridgeRunState.WaitingForController, "Connecting to VIIPER…");
                    try
                    {
                        _viiper?.Dispose();
                        _viiper = new ViiperXbox360Client();
                        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        connectCts.CancelAfter(TimeSpan.FromSeconds(20));
                        await _viiper.ConnectAsync(connectCts.Token);
                        _viiperDep.Ok = true;
                        _viiperDep.Detail = "Connected";
                        _notifiedViiperDown = false;
                    }
                    catch (Exception ex)
                    {
                        _lastError = ex.Message;
                        _viiper?.Dispose();
                        _viiper = null;
                        _viiperDep.Ok = false;
                        var hint = ex.Message.Contains("usbip", StringComparison.OrdinalIgnoreCase)
                            ? " Install usbip-win2 and ensure usbip.exe is on PATH."
                            : "";
                        _viiperDep.Detail =
                            "VIIPER device setup failed: " + ex.Message + hint;
                        PublishStatus(BridgeRunState.Error, _viiperDep.Detail);
                        await Task.Delay(2000, ct);
                        continue;
                    }

                    _activeDriver.PrepareExclusive();
                    _lastKeepalive = DateTime.UtcNow;
                }

                if (_activeDriver.TryRead(out var frame))
                {
                    var profile = _resolvedProfile ?? _profiles.ActiveProfile;
                    var gameBarOpen = IsGameBarOverrideActive();
                    var mapProfile = gameBarOpen ? _gameBarGamepadProfile : profile;
                    var xbox = _mapper.Map(frame, mapProfile);
                    _viiper!.SendInput(xbox);

                    if (_mapper.TryConsumeMouseDelta(out int dx, out int dy))
                        MouseInjector.Move(dx, dy);

                    lock (_gate)
                        _pressed = frame.PressedDigitalIds().ToList();

                    // Throttle status fan-out — per-frame publish raced Game Bar file IPC reads.
                    if ((DateTime.UtcNow - _lastBridgePublish).TotalMilliseconds >= 100)
                    {
                        var src = _profileSource == ActiveProfileSource.GameRule ? "game" : "manual";
                        var msg = gameBarOpen
                            ? $"Bridging — Gamepad (Game Bar open); profile {profile.Name}"
                            : $"Bridging — {profile.Name} ({src})";
                        PublishStatus(BridgeRunState.Bridging, msg);
                        _lastBridgePublish = DateTime.UtcNow;
                    }
                }
                else if (_activeDriver is null || !_activeDriver.IsConnected)
                {
                    TearDownBridge(restoreExclusive: false);
                    PublishStatus(BridgeRunState.WaitingForController, "Controller disconnected");
                }

                if (_activeDriver is { IsConnected: true } &&
                    (DateTime.UtcNow - _lastKeepalive).TotalMilliseconds > 800)
                {
                    _activeDriver.KeepAlive();
                    _lastKeepalive = DateTime.UtcNow;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                PublishStatus(BridgeRunState.Error, ex.Message);
                await Task.Delay(1000, ct);
            }
        }

        TearDownBridge(restoreExclusive: true);
        _mapper.ReleaseAllInjected();
    }

    async Task RefreshViiperProbeAsync(CancellationToken ct)
    {
        if ((DateTime.UtcNow - _lastViiperProbe).TotalSeconds < 2 && _viiper?.IsConnected == true)
            return;
        _lastViiperProbe = DateTime.UtcNow;
        var (ok, detail) = await ViiperHealth.ProbeAsync(ct);
        var wasOk = _viiperDep.Ok;
        _viiperDep.Ok = ok || _viiper?.IsConnected == true;
        if (!_viiperDep.Ok)
            _viiperDep.Detail = detail;
        else if (ok)
            _viiperDep.Detail = detail;

        if (wasOk && !_viiperDep.Ok)
            _notifiedViiperDown = false;
    }

    public bool ConsumeViiperDownNotification()
    {
        if (_viiperDep.Ok || _notifiedViiperDown) return false;
        _notifiedViiperDown = true;
        return true;
    }

    bool IsGameBarOverrideActive() =>
        _gameBar.IsGameBarOpen || _gameBarWidgetOpen;

    void EnterLizardForLock()
    {
        try { _activeDriver?.RestoreExclusive(); } catch { /* ignore */ }
        TearDownVirtualOnly();
        _mapper.ReleaseAllInjected();
        PublishStatus(BridgeRunState.PausedLocked, "Session locked — lizard mode");
    }

    void TearDownVirtualOnly()
    {
        try { _viiper?.Dispose(); } catch { }
        _viiper = null;
    }

    void TearDownBridge(bool restoreExclusive)
    {
        TearDownVirtualOnly();
        if (_activeDriver is not null)
        {
            try
            {
                if (restoreExclusive && _activeDriver.IsConnected)
                    _activeDriver.RestoreExclusive();
            }
            catch { /* ignore */ }
            _activeDriver.Close();
            _activeDriver = null;
        }
    }

    void PublishStatus(BridgeRunState? state = null, string? message = null)
    {
        BridgeStatus snap;
        lock (_gate)
        {
            if (state.HasValue) _status.State = state.Value;
            if (message is not null) _status.Message = message;
            _status.BridgeEnabled = _profiles.BridgeEnabled;
            _status.ControllerConnected = _activeDriver?.IsConnected == true;
            _status.SteamRunning = _steam.IsSteamRunning;
            _status.SessionLocked = _session.IsLocked;
            _status.ViiperConnected = _viiper?.IsConnected == true;
            _status.GameBarOverrideActive = IsGameBarOverrideActive();
            var profile = _resolvedProfile ?? _profiles.ActiveProfile;
            _status.ActiveProfileId = profile.Id;
            _status.ActiveProfileName = profile.Name;
            _status.ActiveProfileSource = _profileSource.ToString();
            _status.ActiveDriverId = _activeDriver?.Id ?? _drivers.Primary.Id;
            _status.ActiveDriverName = _activeDriver?.DisplayName ?? _drivers.Primary.DisplayName;
            _status.CurrentGameExe = _foreground.ExeName;
            _status.CurrentGamePath = _foreground.Path;
            _status.PressedInputs = _pressed.ToList();
            _status.Dependencies =
            [
                new DependencyStatus
                {
                    Id = _viiperDep.Id,
                    DisplayName = _viiperDep.DisplayName,
                    Ok = _viiperDep.Ok,
                    Detail = _viiperDep.Detail
                }
            ];
            _status.UpdatedAt = DateTimeOffset.UtcNow;
            if (!string.IsNullOrEmpty(_lastError) && _status.State == BridgeRunState.Error && message is null)
                _status.Message = _lastError;
            snap = CloneStatus(_status);
        }
        StatusChanged?.Invoke(snap);
    }

    static BridgeStatus CloneStatus(BridgeStatus s) => new()
    {
        State = s.State,
        BridgeEnabled = s.BridgeEnabled,
        ControllerConnected = s.ControllerConnected,
        SteamRunning = s.SteamRunning,
        SessionLocked = s.SessionLocked,
        ViiperConnected = s.ViiperConnected,
        GameBarOverrideActive = s.GameBarOverrideActive,
        ActiveProfileId = s.ActiveProfileId,
        ActiveProfileName = s.ActiveProfileName,
        ActiveProfileSource = s.ActiveProfileSource,
        ActiveDriverId = s.ActiveDriverId,
        ActiveDriverName = s.ActiveDriverName,
        CurrentGameExe = s.CurrentGameExe,
        CurrentGamePath = s.CurrentGamePath,
        PressedInputs = s.PressedInputs.ToList(),
        Dependencies = s.Dependencies.Select(d => new DependencyStatus
        {
            Id = d.Id,
            DisplayName = d.DisplayName,
            Ok = d.Ok,
            Detail = d.Detail
        }).ToList(),
        Message = s.Message,
        UpdatedAt = s.UpdatedAt
    };

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
        _foreground.Dispose();
        _gameBar.Dispose();
    }
}
