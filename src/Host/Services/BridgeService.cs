using MistMapper.Host.Drivers;
using MistMapper.Host.Mapping;
using MistMapper.Host.Steam;
using MistMapper.Host.Viiper;
using MistMapper.Shared;

namespace MistMapper.Host.Services;

/// <summary>
/// Orchestrates multi-pad driver → map → sinks, Steam pause, session lock, per-game profiles, VIIPER health.
/// </summary>
public sealed class BridgeService : IDisposable
{
    sealed class BridgeSlot : IDisposable
    {
        public string DeviceKey { get; set; } = "";
        public int Order { get; set; }
        public bool Enabled { get; set; } = true;
        public string Model { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string? OverrideProfileId { get; set; }
        public IControllerDriver Driver { get; set; } = null!;
        public MappingEngine Mapper { get; set; } = null!;
        public IViiperClient? Viiper { get; set; }
        public List<string> Pressed { get; set; } = [];
        public ControllerProfile? ResolvedProfile { get; set; }
        public ActiveProfileSource ProfileSource { get; set; } = ActiveProfileSource.Default;

        public void Dispose()
        {
            try { Mapper.ReleaseAllInjected(); } catch { /* ignore */ }
            try { Viiper?.Dispose(); } catch { /* ignore */ }
            Viiper = null;
            try
            {
                if (Driver.IsConnected)
                    Driver.RestoreExclusive();
            }
            catch { /* ignore */ }
            try { Driver.Close(); } catch { /* ignore */ }
            try { Driver.Dispose(); } catch { /* ignore */ }
        }
    }

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
        TearDownAllSlots(restoreExclusive: true);
        PublishStatus(BridgeRunState.Stopped, "Stopped");
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

        if (slot.Driver is SteamControllerDriver steam)
        {
            var ok = await steam.IdentifyAsync(ct);
            if (!ok)
                throw new InvalidOperationException("Rumble failed — this interface may not support haptics.");
            return;
        }

        throw new InvalidOperationException("Identify is only supported for Steam Controllers.");
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
                    ? SteamControllerDevice.DisplayNameForModel(slot.Model)
                    : displayName.Trim();
            }
        }
        PublishStatus();
    }

    public string? MakeControllerProfileUnique(string deviceKey, string? sourceProfileId = null)
    {
        if (string.IsNullOrWhiteSpace(deviceKey))
            throw new ArgumentException("deviceKey required");

        var sourceId = sourceProfileId;
        if (string.IsNullOrWhiteSpace(sourceId))
        {
            var resolved = ResolveProfileForSlot(deviceKey, null);
            sourceId = resolved.profile.Id;
        }

        var slotMeta = _profiles.FindControllerSlot(deviceKey);
        var label = slotMeta?.DisplayName
                    ?? SteamControllerDevice.DisplayNameForModel(slotMeta?.LastModel ?? "sc2");
        var created = _profiles.SaveAsProfile(sourceId!, label + " layout");
        _profiles.SetControllerSlotProfile(deviceKey, created.Id);
        RefreshSlotOverridesFromStore();
        PublishStatus();
        return created.Id;
    }

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
        var profile = GetSelectedResolvedProfile() ?? _resolvedProfile ?? _profiles.ActiveProfile;
        _profiles.BindToGame(profile.Id, exe, null, FriendlyGameName());
        _manualProfileLock = false;
        ResolveProfile();
        PublishStatus();
    }

    public string EnsureLayoutForCurrentGame(string sourceProfileId)
    {
        var exe = _foreground.ExeName;
        if (string.IsNullOrWhiteSpace(exe))
            return sourceProfileId;

        var existing = _profiles.FindBindingForGame(exe, _foreground.Path);
        if (existing is not null)
        {
            if (!string.IsNullOrWhiteSpace(_foreground.DisplayName)
                && !string.Equals(existing.DisplayName, _foreground.DisplayName, StringComparison.Ordinal))
            {
                _profiles.BindToGame(existing.ProfileId, exe, existing.MatchPathContains, FriendlyGameName());
            }

            _manualProfileLock = false;
            ResolveProfile();
            var bound = _resolvedProfile ?? _profiles.ActiveProfile;
            return bound.Id;
        }

        var name = FriendlyGameName();
        var created = _profiles.SaveAsProfile(sourceProfileId, name);
        _profiles.BindToGame(created.Id, exe, null, name);
        _manualProfileLock = false;
        ResolveProfile();
        PublishStatus(message: "Created layout for " + name);
        return created.Id;
    }

    /// <summary>Profile the widget should edit for the selected pad.</summary>
    public ControllerProfile GetSelectedResolvedProfile()
    {
        string key;
        lock (_gate) key = _selectedDeviceKey;
        BridgeSlot? slot;
        lock (_gate)
            slot = FindSlotUnlocked(key) ?? PrimarySlotUnlocked();

        if (slot is not null)
            return ResolveProfileForSlot(slot.DeviceKey, slot.OverrideProfileId).profile;

        return _resolvedProfile ?? _profiles.ActiveProfile;
    }

    /// <summary>
    /// Remap target for the selected pad: pad override as-is, otherwise Steam-style game layout ensure.
    /// </summary>
    public string ResolveRemapTargetProfileId(string sourceProfileId)
    {
        string key;
        lock (_gate) key = _selectedDeviceKey;
        BridgeSlot? slot;
        lock (_gate)
            slot = FindSlotUnlocked(key) ?? PrimarySlotUnlocked();

        if (slot is not null && !string.IsNullOrWhiteSpace(slot.OverrideProfileId))
            return slot.OverrideProfileId!;

        return EnsureLayoutForCurrentGame(sourceProfileId);
    }

    public bool SelectedPadHasProfileOverride()
    {
        string key;
        lock (_gate) key = _selectedDeviceKey;
        lock (_gate)
        {
            var slot = FindSlotUnlocked(key) ?? PrimarySlotUnlocked();
            return slot is not null && !string.IsNullOrWhiteSpace(slot.OverrideProfileId);
        }
    }

    string FriendlyGameName()
    {
        var name = _foreground.DisplayName;
        if (string.IsNullOrWhiteSpace(name))
            name = GameDisplayName.Resolve(_foreground.Path, _foreground.ExeName);
        return name;
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

    (ControllerProfile profile, ActiveProfileSource source, bool hasOverride) ResolveProfileForSlot(
        string deviceKey,
        string? overrideProfileId)
    {
        if (!string.IsNullOrWhiteSpace(overrideProfileId))
        {
            var overrideProfile = _profiles.GetProfiles()
                .FirstOrDefault(p => p.Id.Equals(overrideProfileId, StringComparison.OrdinalIgnoreCase));
            if (overrideProfile is not null)
                return (overrideProfile, ActiveProfileSource.Manual, true);
        }

        if (_manualProfileLock)
            return (_profiles.ActiveProfile, ActiveProfileSource.Manual, false);

        var (profile, source) = _profiles.ResolveForGame(_foreground.ExeName, _foreground.Path);
        return (profile, source, false);
    }

    void RefreshSlotOverridesFromStore()
    {
        lock (_gate)
        {
            foreach (var slot in _slots)
            {
                var stored = _profiles.FindControllerSlot(slot.DeviceKey);
                if (stored is null) continue;
                slot.Order = stored.Order;
                slot.Enabled = stored.Enabled;
                slot.OverrideProfileId = stored.ProfileId;
                if (!string.IsNullOrWhiteSpace(stored.DisplayName))
                    slot.DisplayName = stored.DisplayName!;
            }
            _slots.Sort((a, b) => a.Order.CompareTo(b.Order));
        }
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

                    var (profile, source, _) = ResolveProfileForSlot(slot.DeviceKey, slot.OverrideProfileId);
                    slot.ResolvedProfile = profile;
                    slot.ProfileSource = source;

                    var gameBarOpen = IsGameBarOverrideActive();
                    var mapProfile = gameBarOpen ? _gameBarGamepadProfile : profile;
                    var allowKbMouse = string.Equals(slot.DeviceKey, primaryKey, StringComparison.OrdinalIgnoreCase);
                    var xbox = slot.Mapper.Map(frame, mapProfile, allowKbMouse);
                    slot.Viiper!.SendInput(xbox);

                    if (allowKbMouse && slot.Mapper.TryConsumeMouseDelta(out int dx, out int dy))
                        _mouse.Move(dx, dy);

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
                PublishStatus(BridgeRunState.Error, ex.Message);
                await Task.Delay(1000, ct);
            }
        }

        TearDownAllSlots(restoreExclusive: true);
    }

    string? PrimaryKeyboardMouseDeviceKey()
    {
        lock (_gate)
        {
            return _slots
                .Where(s => s.Enabled && s.Driver.IsConnected)
                .OrderBy(s => s.Order)
                .Select(s => s.DeviceKey)
                .FirstOrDefault();
        }
    }

    BridgeSlot? PrimarySlotUnlocked() =>
        _slots.Where(s => s.Driver.IsConnected).OrderBy(s => s.Order).FirstOrDefault()
        ?? _slots.OrderBy(s => s.Order).FirstOrDefault();

    BridgeSlot? FindSlotUnlocked(string deviceKey)
    {
        if (string.IsNullOrEmpty(deviceKey)) return null;
        return _slots.FirstOrDefault(s =>
            s.DeviceKey.Equals(deviceKey, StringComparison.OrdinalIgnoreCase));
    }

    async Task SyncSlotsAsync(CancellationToken ct)
    {
        if (_drivers.UsesInjectedDrivers)
            await SyncInjectedSlotsAsync(ct);
        else
            await SyncPhysicalSlotsAsync(ct);

        lock (_gate)
        {
            if (string.IsNullOrEmpty(_selectedDeviceKey)
                || FindSlotUnlocked(_selectedDeviceKey) is null)
            {
                _selectedDeviceKey = PrimarySlotUnlocked()?.DeviceKey ?? "";
            }
        }
    }

    async Task SyncInjectedSlotsAsync(CancellationToken ct)
    {
        var connectedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var driver in _drivers.InjectedDrivers)
        {
            if (!driver.IsConnected)
                driver.TryOpen();
            if (!driver.IsConnected) continue;

            var key = string.IsNullOrEmpty(driver.DeviceKey) ? driver.Id : driver.DeviceKey;
            connectedKeys.Add(key);
            BridgeSlot? existing;
            lock (_gate)
                existing = FindSlotUnlocked(key);

            if (existing is not null) continue;

            var stored = _profiles.EnsureControllerSlot(
                key,
                driver.DisplayName,
                driver.ControllerModel);
            var slot = new BridgeSlot
            {
                DeviceKey = key,
                Order = stored.Order,
                Enabled = stored.Enabled,
                Model = driver.ControllerModel,
                DisplayName = stored.DisplayName ?? driver.DisplayName,
                OverrideProfileId = stored.ProfileId,
                Driver = driver,
                Mapper = new MappingEngine(_keyboard, _mouse)
            };
            lock (_gate)
            {
                _slots.Add(slot);
                _slots.Sort((a, b) => a.Order.CompareTo(b.Order));
            }
            await EnsureViiperForSlotAsync(slot, ct);
        }

        RemoveMissingSlots(connectedKeys);
    }

    async Task SyncPhysicalSlotsAsync(CancellationToken ct)
    {
        HashSet<string> claimed;
        lock (_gate)
            claimed = _slots.Select(s => s.DeviceKey).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Drop disconnected first so Enumerate can see them again if replugged.
        List<BridgeSlot> gone;
        lock (_gate)
            gone = _slots.Where(s => !s.Driver.IsConnected).ToList();
        foreach (var slot in gone)
            RemoveSlot(slot);

        lock (_gate)
            claimed = _slots.Select(s => s.DeviceKey).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var available = DriverRegistry.EnumeratePhysicalPads(claimed);
        foreach (var (deviceKey, devicePath, model) in available)
        {
            if (claimed.Contains(deviceKey)) continue;
            var driver = DriverRegistry.OpenSteamController(devicePath);
            if (driver is null) continue;

            var stored = _profiles.EnsureControllerSlot(
                deviceKey,
                driver.DisplayName,
                model);
            var slot = new BridgeSlot
            {
                DeviceKey = deviceKey,
                Order = stored.Order,
                Enabled = stored.Enabled,
                Model = model,
                DisplayName = stored.DisplayName ?? driver.DisplayName,
                OverrideProfileId = stored.ProfileId,
                Driver = driver,
                Mapper = new MappingEngine(_keyboard, _mouse)
            };
            lock (_gate)
            {
                _slots.Add(slot);
                _slots.Sort((a, b) => a.Order.CompareTo(b.Order));
            }
            claimed.Add(deviceKey);
            await EnsureViiperForSlotAsync(slot, ct);
        }

        var connectedKeys = new HashSet<string>(
            SteamControllerDevice.EnumerateInstances()
                .Select(d => SteamControllerDevice.PhysicalDeviceKey(d.DevicePath!)),
            StringComparer.OrdinalIgnoreCase);
        // Keep slots whose drivers are still open even if enumerate briefly misses them.
        lock (_gate)
        {
            foreach (var s in _slots.Where(s => s.Driver.IsConnected))
                connectedKeys.Add(s.DeviceKey);
        }
        RemoveMissingSlots(connectedKeys);
    }

    void RemoveMissingSlots(HashSet<string> connectedKeys)
    {
        List<BridgeSlot> remove;
        lock (_gate)
            remove = _slots.Where(s => !connectedKeys.Contains(s.DeviceKey)).ToList();
        foreach (var slot in remove)
            RemoveSlot(slot);
    }

    void RemoveSlot(BridgeSlot slot)
    {
        lock (_gate)
            _slots.Remove(slot);
        try { slot.Dispose(); } catch { /* ignore */ }
    }

    async Task<bool> EnsureViiperForSlotAsync(BridgeSlot slot, CancellationToken ct)
    {
        if (slot.Viiper?.IsConnected == true)
            return true;

        try
        {
            slot.Viiper?.Dispose();
            slot.Viiper = _viiperFactory();
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectCts.CancelAfter(TimeSpan.FromSeconds(20));
            await slot.Viiper.ConnectAsync(connectCts.Token);
            _viiperDep.Ok = true;
            _viiperDep.Detail = "Connected";
            _notifiedViiperDown = false;
            slot.Driver.PrepareExclusive();
            return true;
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            try { slot.Viiper?.Dispose(); } catch { /* ignore */ }
            slot.Viiper = null;
            _viiperDep.Ok = false;
            var hint = ex.Message.Contains("usbip", StringComparison.OrdinalIgnoreCase)
                ? " Install usbip-win2 and ensure usbip.exe is on PATH."
                : "";
            _viiperDep.Detail = "VIIPER device setup failed: " + ex.Message + hint;
            PublishStatus(BridgeRunState.Error, _viiperDep.Detail);
            return false;
        }
    }

    async Task RefreshViiperProbeAsync(CancellationToken ct)
    {
        var anyConnected = false;
        lock (_gate)
            anyConnected = _slots.Any(s => s.Viiper?.IsConnected == true);

        if ((DateTime.UtcNow - _lastViiperProbe).TotalSeconds < 2 && anyConnected)
            return;
        _lastViiperProbe = DateTime.UtcNow;
        var (ok, detail) = await _viiperHealth.ProbeAsync(ct);
        var wasOk = _viiperDep.Ok;
        _viiperDep.Ok = ok || anyConnected;
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

    static bool IsGameBarOverrideActive() => false;

    void EnterLizardForLock()
    {
        TearDownAllSlots(restoreExclusive: true);
        PublishStatus(BridgeRunState.PausedLocked, "Session locked — lizard mode");
    }

    void TearDownVirtualOnly()
    {
        lock (_gate)
        {
            foreach (var slot in _slots)
            {
                try { slot.Viiper?.Dispose(); } catch { /* ignore */ }
                slot.Viiper = null;
                try { slot.Mapper.ReleaseAllInjected(); } catch { /* ignore */ }
            }
        }
    }

    void ReleaseAllMappers()
    {
        lock (_gate)
        {
            foreach (var slot in _slots)
            {
                try { slot.Mapper.ReleaseAllInjected(); } catch { /* ignore */ }
            }
        }
    }

    void TearDownAllSlots(bool restoreExclusive)
    {
        List<BridgeSlot> copy;
        lock (_gate)
        {
            copy = _slots.ToList();
            _slots.Clear();
        }
        foreach (var slot in copy)
        {
            if (!restoreExclusive)
            {
                try { slot.Viiper?.Dispose(); } catch { /* ignore */ }
                slot.Viiper = null;
                try { slot.Mapper.ReleaseAllInjected(); } catch { /* ignore */ }
                try { slot.Driver.Close(); } catch { /* ignore */ }
            }
            else
            {
                try { slot.Dispose(); } catch { /* ignore */ }
            }
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
            _status.AutoPauseWhenSteamRunning = _profiles.AutoPauseWhenSteamRunning;

            var connected = _slots.Where(s => s.Driver.IsConnected).OrderBy(s => s.Order).ToList();
            var primary = connected.FirstOrDefault() ?? PrimarySlotUnlocked();
            _status.ControllerConnected = connected.Count > 0;
            _status.ControllerModel = primary?.Model
                ?? (primary?.Driver as SteamControllerDriver)?.ControllerModel
                ?? "";
            _status.SteamRunning = _steam.IsSteamRunning;
            _status.SessionLocked = _session.IsLocked;
            _status.ViiperConnected = _slots.Any(s => s.Viiper?.IsConnected == true);
            _status.GameBarOverrideActive = IsGameBarOverrideActive();

            var selected = FindSlotUnlocked(_selectedDeviceKey) ?? primary;
            var selectedResolved = selected is null
                ? (_resolvedProfile ?? _profiles.ActiveProfile)
                : ResolveProfileForSlot(selected.DeviceKey, selected.OverrideProfileId).profile;
            var selectedSource = selected?.ProfileSource ?? _profileSource;

            _status.ActiveProfileId = selectedResolved.Id;
            _status.ActiveProfileName = selectedResolved.Name;
            _status.ActiveProfileSource = selectedSource.ToString();
            _status.ActiveDriverId = primary?.Driver.Id ?? DriverIds.SteamController;
            _status.ActiveDriverName = primary?.DisplayName
                ?? primary?.Driver.DisplayName
                ?? "Steam Controller";
            _status.CurrentGameExe = _foreground.ExeName;
            _status.CurrentGamePath = _foreground.Path;
            _status.CurrentGameName = string.IsNullOrWhiteSpace(_foreground.DisplayName)
                ? GameDisplayName.Resolve(_foreground.Path, _foreground.ExeName)
                : _foreground.DisplayName;
            _status.PressedInputs = selected?.Pressed.ToList()
                ?? primary?.Pressed.ToList()
                ?? [];
            _status.SelectedDeviceKey = selected?.DeviceKey ?? _selectedDeviceKey;
            _status.Controllers = connected.Select(s =>
            {
                var (prof, _, hasOverride) = ResolveProfileForSlot(s.DeviceKey, s.OverrideProfileId);
                return new ControllerStatus
                {
                    DeviceKey = s.DeviceKey,
                    Model = s.Model,
                    DisplayName = string.IsNullOrWhiteSpace(s.DisplayName)
                        ? SteamControllerDevice.DisplayNameForModel(s.Model)
                        : s.DisplayName,
                    Order = s.Order,
                    Enabled = s.Enabled,
                    Connected = true,
                    ProfileId = prof.Id,
                    ProfileName = prof.Name,
                    HasProfileOverride = hasOverride || !string.IsNullOrEmpty(s.OverrideProfileId),
                    PressedInputs = s.Pressed.ToList()
                };
            }).ToList();

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
        AutoPauseWhenSteamRunning = s.AutoPauseWhenSteamRunning,
        ControllerConnected = s.ControllerConnected,
        ControllerModel = s.ControllerModel,
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
        CurrentGameName = s.CurrentGameName,
        PressedInputs = s.PressedInputs.ToList(),
        SelectedDeviceKey = s.SelectedDeviceKey,
        Controllers = s.Controllers.Select(c => new ControllerStatus
        {
            DeviceKey = c.DeviceKey,
            Model = c.Model,
            DisplayName = c.DisplayName,
            Order = c.Order,
            Enabled = c.Enabled,
            Connected = c.Connected,
            ProfileId = c.ProfileId,
            ProfileName = c.ProfileName,
            HasProfileOverride = c.HasProfileOverride,
            PressedInputs = c.PressedInputs.ToList()
        }).ToList(),
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
