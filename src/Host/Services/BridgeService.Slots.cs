using MistMapper.Host.Drivers;
using MistMapper.Host.Logging;
using MistMapper.Host.Mapping;
using MistMapper.Host.Steam;
using MistMapper.Host.Viiper;
using MistMapper.Shared;

namespace MistMapper.Host.Services;

public sealed partial class BridgeService
{
    sealed class BridgeSlot : IDisposable
    {
        public string DeviceKey { get; set; } = "";
        public int Order { get; set; }
        public bool Enabled { get; set; } = true;
        public bool RumbleEnabled { get; set; } = true;
        public string Model { get; set; } = "";
        public string DisplayName { get; set; } = "";
        /// <summary>Per-pad profile override (null = shared/game resolve).</summary>
        public string? ProfileId { get; set; }
        public IControllerDriver Driver { get; set; } = null!;
        public MappingEngine Mapper { get; set; } = null!;
        public IViiperClient? Viiper { get; set; }
        public Action<byte, byte>? RumbleHandler { get; set; }
        public List<string> Pressed { get; set; } = [];
        public ControllerProfile? ResolvedProfile { get; set; }
        public ActiveProfileSource ProfileSource { get; set; } = ActiveProfileSource.Default;

        public void Dispose()
        {
            UnhookRumble();
            try { Driver.SetRumble(0, 0); } catch { /* ignore */ }
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

        public void UnhookRumble()
        {
            if (Viiper is null || RumbleHandler is null) return;
            try { Viiper.RumbleReceived -= RumbleHandler; } catch { /* ignore */ }
            RumbleHandler = null;
        }

        public void HookRumble()
        {
            UnhookRumble();
            if (Viiper is null) return;
            var driver = Driver;
            RumbleHandler = (left, right) =>
            {
                try
                {
                    if (!RumbleEnabled)
                    {
                        driver.SetRumble(0, 0);
                        return;
                    }
                    driver.SetRumble(left, right);
                }
                catch { /* ignore */ }
            };
            Viiper.RumbleReceived += RumbleHandler;
        }
    }

    string? PrimaryKeyboardMouseDeviceKey()
    {
        lock (_gate)
        {
            // Prefer the pad selected in the widget so DualSense (or any secondary
            // pad) can drive AsMouse gyro / trackpad without being Order 0.
            var selected = FindSlotUnlocked(_selectedDeviceKey);
            if (selected is not null && selected.Enabled && selected.Driver.IsConnected)
                return selected.DeviceKey;

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
                driver.ControllerModel,
                driver.Id);
            var slot = new BridgeSlot
            {
                DeviceKey = key,
                Order = stored.Order,
                Enabled = stored.Enabled,
                RumbleEnabled = stored.RumbleEnabled,
                Model = driver.ControllerModel,
                DisplayName = stored.DisplayName ?? driver.DisplayName,
                ProfileId = stored.ProfileId,
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
        foreach (var (deviceKey, devicePath, model, driverId) in available)
        {
            if (claimed.Contains(deviceKey)) continue;
            var driver = DriverRegistry.OpenPad(driverId, devicePath);
            if (driver is null) continue;

            var stored = _profiles.EnsureControllerSlot(
                deviceKey,
                driver.DisplayName,
                model,
                driverId);
            var slot = new BridgeSlot
            {
                DeviceKey = deviceKey,
                Order = stored.Order,
                Enabled = stored.Enabled,
                RumbleEnabled = stored.RumbleEnabled,
                Model = model,
                DisplayName = stored.DisplayName ?? driver.DisplayName,
                ProfileId = stored.ProfileId,
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

        var connectedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in SteamControllerDevice.EnumerateInstances())
            connectedKeys.Add(SteamControllerDevice.PhysicalDeviceKey(d.DevicePath!));
        foreach (var d in DualSense.DualSenseDevice.EnumerateInstances())
            connectedKeys.Add(DualSense.DualSenseDevice.PhysicalDeviceKey(d.DevicePath!));
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
            slot.UnhookRumble();
            try { slot.Driver.SetRumble(0, 0); } catch { /* ignore */ }
            try { slot.Viiper?.Dispose(); } catch { /* ignore */ }
            slot.Viiper = _viiperFactory();
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectCts.CancelAfter(TimeSpan.FromSeconds(20));
            await slot.Viiper.ConnectAsync(connectCts.Token);
            slot.HookRumble();
            _viiperDep.Ok = true;
            _viiperDep.Detail = "Connected";
            _notifiedViiperDown = false;
            if (!slot.Driver.PrepareExclusive()
                && string.Equals(slot.Driver.Id, DriverIds.DualSense, StringComparison.OrdinalIgnoreCase))
            {
                _lastError =
                    "DualSense native pad still visible to games (double input). " +
                    "Restart MistMapper as Administrator to hide it.";
                PublishStatus(BridgeRunState.Bridging, _lastError);
            }
            return true;
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            AppLog.Current.Error("VIIPER setup failed for " + slot.DeviceKey, ex);
            slot.UnhookRumble();
            try { slot.Viiper?.Dispose(); } catch { /* ignore */ }
            slot.Viiper = null;
            // Leave _viiperDep.Ok alone when the API is up — device/usbip attach can lag on
            // cold Xbox-mode boots. Marking Ok=false made the loop skip reconnect and sit in
            // "VIIPER unavailable" until the user toggled the bridge.
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
                slot.UnhookRumble();
                try { slot.Driver.SetRumble(0, 0); } catch { /* ignore */ }
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
                slot.UnhookRumble();
                try { slot.Driver.SetRumble(0, 0); } catch { /* ignore */ }
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
}
