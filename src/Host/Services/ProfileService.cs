using System.Text.Json;
using MistMapper.Shared;

namespace MistMapper.Host.Services;

public sealed class ProfileService
{
    readonly string _path;
    readonly object _lock = new();
    ProfileStoreDocument _doc;

    public event Action? Changed;

    public ProfileService(string? directory = null)
    {
        var usingDefaultDir = directory is null;
        var dir = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MistMapper");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "profiles.json");
        if (usingDefaultDir)
            MigrateLegacyAppDataIfNeeded(dir);
        _doc = LoadOrCreate();
    }

    /// <summary>One-shot copy from pre-rename %AppData%\SteamControllerBridge.</summary>
    static void MigrateLegacyAppDataIfNeeded(string newDir)
    {
        var newProfiles = Path.Combine(newDir, "profiles.json");
        if (File.Exists(newProfiles)) return;

        var legacyDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SteamControllerBridge");
        var legacyProfiles = Path.Combine(legacyDir, "profiles.json");
        if (!File.Exists(legacyProfiles)) return;

        try
        {
            File.Copy(legacyProfiles, newProfiles);
            foreach (var file in Directory.EnumerateFiles(legacyDir))
            {
                var name = Path.GetFileName(file);
                if (string.Equals(name, "profiles.json", StringComparison.OrdinalIgnoreCase))
                    continue;
                var dest = Path.Combine(newDir, name);
                if (!File.Exists(dest))
                    File.Copy(file, dest);
            }
        }
        catch
        {
            // Fall through to LoadOrCreate defaults if migration fails.
        }
    }

    public ProfileStoreDocument Document
    {
        get { lock (_lock) return Clone(_doc); }
    }

    public ControllerProfile ActiveProfile
    {
        get
        {
            lock (_lock)
            {
                return CloneProfile(_doc.Profiles.FirstOrDefault(p => p.Id == _doc.ActiveProfileId)
                       ?? _doc.Profiles.First());
            }
        }
    }

    public bool BridgeEnabled
    {
        get { lock (_lock) return _doc.BridgeEnabled; }
        set
        {
            lock (_lock)
            {
                _doc.BridgeEnabled = value;
                SaveUnlocked();
            }
            Changed?.Invoke();
        }
    }

    public bool AutoPauseWhenSteamRunning
    {
        get { lock (_lock) return _doc.AutoPauseWhenSteamRunning; }
        set
        {
            lock (_lock)
            {
                if (_doc.AutoPauseWhenSteamRunning == value) return;
                _doc.AutoPauseWhenSteamRunning = value;
                SaveUnlocked();
            }
            Changed?.Invoke();
        }
    }

    public IReadOnlyList<ControllerProfile> GetProfiles()
    {
        lock (_lock) return _doc.Profiles.Select(CloneProfile).ToList();
    }

    public IReadOnlyList<ProfileBinding> GetBindings()
    {
        lock (_lock) return _doc.ProfileBindings.Select(CloneBinding).ToList();
    }

    /// <summary>Returns the binding that would win for this foreground process, if any.</summary>
    public ProfileBinding? FindBindingForGame(string exe, string path)
    {
        lock (_lock)
        {
            ProfileBinding? bestPath = null;
            ProfileBinding? bestExe = null;
            foreach (var b in _doc.ProfileBindings)
            {
                if (!string.IsNullOrEmpty(b.MatchPathContains) &&
                    !string.IsNullOrEmpty(path) &&
                    path.Contains(b.MatchPathContains, StringComparison.OrdinalIgnoreCase) &&
                    (string.IsNullOrEmpty(b.MatchExe) || b.MatchExe.Equals(exe, StringComparison.OrdinalIgnoreCase) ||
                     Path.GetFileName(path).Equals(b.MatchExe, StringComparison.OrdinalIgnoreCase)))
                {
                    bestPath = b;
                    break;
                }
                if (b.MatchExe.Equals(exe, StringComparison.OrdinalIgnoreCase))
                    bestExe ??= b;
            }

            var hit = bestPath ?? bestExe;
            return hit is null ? null : CloneBinding(hit);
        }
    }

    public void SetActiveProfile(string id)
    {
        lock (_lock)
        {
            if (_doc.Profiles.All(p => p.Id != id))
                throw new ArgumentException("Unknown profile id");
            _doc.ActiveProfileId = id;
            SaveUnlocked();
        }
        Changed?.Invoke();
    }

    public ControllerProfile Upsert(ControllerProfile profile)
    {
        lock (_lock)
        {
            profile.MigrateLegacyButtonMap();
            var idx = _doc.Profiles.FindIndex(p => p.Id == profile.Id);
            if (idx >= 0) _doc.Profiles[idx] = CloneProfile(profile);
            else _doc.Profiles.Add(CloneProfile(profile));
            if (string.IsNullOrEmpty(_doc.ActiveProfileId))
                _doc.ActiveProfileId = profile.Id;
            SaveUnlocked();
            var result = CloneProfile(profile);
            Changed?.Invoke();
            return result;
        }
    }

    public void Delete(string id)
    {
        lock (_lock)
        {
            if (_doc.Profiles.Count <= 1)
                throw new InvalidOperationException("Cannot delete the last profile");
            _doc.Profiles.RemoveAll(p => p.Id == id);
            _doc.ProfileBindings.RemoveAll(b => b.ProfileId == id);
            if (_doc.ActiveProfileId == id)
                _doc.ActiveProfileId = _doc.Profiles[0].Id;
            SaveUnlocked();
        }
        Changed?.Invoke();
    }

    public ControllerProfile CreateFromLayout(string layoutId, string? customName = null)
    {
        lock (_lock)
        {
            var profile = OfficialLayouts.Create(layoutId, customName);
            profile.IsOfficial = false;
            profile.Name = UniqueName(profile.Name);
            _doc.Profiles.Add(CloneProfile(profile));
            _doc.ActiveProfileId = profile.Id;
            SaveUnlocked();
            var result = CloneProfile(profile);
            Changed?.Invoke();
            return result;
        }
    }

    /// <summary>Steam-style Apply: copy an official template onto an existing user profile in-place.</summary>
    public ControllerProfile ApplyLayout(string profileId, string layoutId)
    {
        lock (_lock)
        {
            var target = _doc.Profiles.FirstOrDefault(p => p.Id == profileId)
                         ?? throw new ArgumentException("Unknown profile id");
            var template = OfficialLayouts.Create(layoutId);
            CopyLayoutContent(target, template);
            target.LayoutId = layoutId;
            target.IsOfficial = false;
            SaveUnlocked();
            var result = CloneProfile(target);
            Changed?.Invoke();
            return result;
        }
    }

    /// <summary>Clone the current layout into a new named user profile and activate it.</summary>
    public ControllerProfile SaveAsProfile(string sourceProfileId, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name required");
        lock (_lock)
        {
            var src = _doc.Profiles.FirstOrDefault(p => p.Id == sourceProfileId)
                      ?? throw new ArgumentException("Unknown profile id");
            var copy = CloneProfile(src);
            copy.Id = Guid.NewGuid().ToString("N");
            copy.Name = UniqueName(name.Trim());
            copy.IsOfficial = false;
            _doc.Profiles.Add(copy);
            _doc.ActiveProfileId = copy.Id;
            SaveUnlocked();
            var result = CloneProfile(copy);
            Changed?.Invoke();
            return result;
        }
    }

    /// <summary>Display strings for an official template (preview before apply).</summary>
    public static Dictionary<string, string> PreviewLayoutMap(string layoutId)
    {
        var template = OfficialLayouts.Create(layoutId);
        template.MigrateLegacyButtonMap();
        return template.InputMap.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.ToDisplayString(),
            StringComparer.OrdinalIgnoreCase);
    }

    public static string LayoutDisplayName(string? layoutId)
    {
        if (string.IsNullOrEmpty(layoutId)) return "Custom";
        var info = OfficialLayouts.All.FirstOrDefault(l =>
            l.Id.Equals(layoutId, StringComparison.OrdinalIgnoreCase));
        return info?.Name ?? layoutId;
    }

    /// <summary>User-editable profiles only (official templates are catalog-only).</summary>
    public IReadOnlyList<ControllerProfile> GetUserProfiles()
    {
        lock (_lock)
            return _doc.Profiles.Where(p => !p.IsOfficial).Select(CloneProfile).ToList();
    }

    static void CopyLayoutContent(ControllerProfile target, ControllerProfile template)
    {
        template.MigrateLegacyButtonMap();
        target.InputMap = template.InputMap.ToDictionary(
            kv => kv.Key,
            kv => CloneAction(kv.Value),
            StringComparer.OrdinalIgnoreCase);
        target.LeftTrackpad = template.LeftTrackpad;
        target.RightTrackpad = template.RightTrackpad;
        target.LeftTrackpadSettings = TrackpadSurfaceSettings.Clone(template.LeftTrackpadSettings);
        target.RightTrackpadSettings = TrackpadSurfaceSettings.Clone(template.RightTrackpadSettings);
        target.Gyro = template.Gyro;
        target.GyroSensitivity = template.GyroSensitivity;
        target.GyroButtons = template.GyroButtons.ToList();
        target.GyroButtonMode = template.GyroButtonMode;
        target.GyroButtonCombine = template.GyroButtonCombine;
        target.GyroDotsPer360 = template.GyroDotsPer360;
        target.StickDeadzone = template.StickDeadzone;
        target.TriggerDeadzone = template.TriggerDeadzone;
        target.TrackpadDeadzone = template.TrackpadDeadzone;
        target.StickSensitivityX = template.StickSensitivityX;
        target.StickSensitivityY = template.StickSensitivityY;
        target.TrackpadSensitivityX = template.TrackpadSensitivityX;
        target.TrackpadSensitivityY = template.TrackpadSensitivityY;
        target.GyroSensitivityX = template.GyroSensitivityX;
        target.GyroSensitivityY = template.GyroSensitivityY;
        target.InvertStickX = template.InvertStickX;
        target.InvertStickY = template.InvertStickY;
        target.InvertTrackpadX = template.InvertTrackpadX;
        target.InvertTrackpadY = template.InvertTrackpadY;
        target.InvertGyroX = template.InvertGyroX;
        target.InvertGyroY = template.InvertGyroY;
        target.EnsureLockedMappings();
    }

    public ControllerProfile Duplicate(string profileId, string? customName = null)
    {
        lock (_lock)
        {
            var src = _doc.Profiles.FirstOrDefault(p => p.Id == profileId)
                      ?? throw new ArgumentException("Unknown profile id");
            var copy = CloneProfile(src);
            copy.Id = Guid.NewGuid().ToString("N");
            copy.Name = string.IsNullOrWhiteSpace(customName)
                ? UniqueName(src.Name + " copy")
                : customName.Trim();
            copy.IsOfficial = false;
            _doc.Profiles.Add(copy);
            _doc.ActiveProfileId = copy.Id;
            SaveUnlocked();
            var result = CloneProfile(copy);
            Changed?.Invoke();
            return result;
        }
    }

    public void Rename(string profileId, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name required");
        lock (_lock)
        {
            var p = _doc.Profiles.First(x => x.Id == profileId);
            p.Name = name.Trim();
            p.IsOfficial = false;
            SaveUnlocked();
        }
        Changed?.Invoke();
    }

    /// <summary>No longer seeds official templates into the profile store (catalog-only).</summary>
    public void EnsureOfficialLayouts()
    {
        lock (_lock)
        {
            NormalizeUserProfilesUnlocked();
            SaveUnlocked();
        }
        Changed?.Invoke();
    }

    string UniqueName(string baseName)
    {
        var name = baseName;
        var i = 2;
        while (_doc.Profiles.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            name = $"{baseName} ({i++})";
        return name;
    }

    public void Remap(string profileId, PhysicalInput physical, XboxOutput xbox) =>
        RemapAction(profileId, physical.ToString(), OutputAction.FromXbox(xbox));

    public void RemapAction(string profileId, string inputId, OutputAction action)
    {
        if (MappingLocks.IsLockedGuideInput(inputId))
            throw new InvalidOperationException("Steam / Guide is locked to Xbox Guide and cannot be remapped.");

        lock (_lock)
        {
            var p = _doc.Profiles.First(x => x.Id == profileId);
            p.SetAction(inputId, action);
            p.EnsureLockedMappings();
            p.IsOfficial = false;
            SaveUnlocked();
        }
        Changed?.Invoke();
    }

    public void SetTrackpad(string profileId, bool left, TrackpadMode mode)
    {
        lock (_lock)
        {
            var p = _doc.Profiles.First(x => x.Id == profileId);
            if (left) p.LeftTrackpad = mode;
            else p.RightTrackpad = mode;
            p.IsOfficial = false;
            SaveUnlocked();
        }
        Changed?.Invoke();
    }

    public void SetGyro(string profileId, GyroMode mode, float? sensitivity = null)
    {
        lock (_lock)
        {
            var p = _doc.Profiles.First(x => x.Id == profileId);
            p.Gyro = mode;
            if (sensitivity.HasValue) p.GyroSensitivity = sensitivity.Value;
            p.IsOfficial = false;
            SaveUnlocked();
        }
        Changed?.Invoke();
    }

    public void SetSensitivity(string profileId, SensitivityPayload s)
    {
        lock (_lock)
        {
            var p = _doc.Profiles.First(x => x.Id == profileId);
            if (s.StickDeadzone.HasValue) p.StickDeadzone = s.StickDeadzone.Value;
            if (s.TriggerDeadzone.HasValue) p.TriggerDeadzone = s.TriggerDeadzone.Value;
            if (s.TrackpadDeadzone.HasValue) p.TrackpadDeadzone = s.TrackpadDeadzone.Value;
            if (s.StickSensitivityX.HasValue) p.StickSensitivityX = s.StickSensitivityX.Value;
            if (s.StickSensitivityY.HasValue) p.StickSensitivityY = s.StickSensitivityY.Value;
            if (s.TrackpadSensitivityX.HasValue) p.TrackpadSensitivityX = s.TrackpadSensitivityX.Value;
            if (s.TrackpadSensitivityY.HasValue) p.TrackpadSensitivityY = s.TrackpadSensitivityY.Value;
            if (s.GyroSensitivity.HasValue) p.GyroSensitivity = s.GyroSensitivity.Value;
            if (s.GyroSensitivityX.HasValue) p.GyroSensitivityX = s.GyroSensitivityX.Value;
            if (s.GyroSensitivityY.HasValue) p.GyroSensitivityY = s.GyroSensitivityY.Value;
            if (s.GyroDotsPer360.HasValue) p.GyroDotsPer360 = Math.Clamp(s.GyroDotsPer360.Value, 500f, 20000f);
            if (!string.IsNullOrWhiteSpace(s.GyroButtonMode)
                && Enum.TryParse<GyroButtonMode>(s.GyroButtonMode, true, out var gbm))
                p.GyroButtonMode = gbm;
            if (!string.IsNullOrWhiteSpace(s.GyroButtonCombine)
                && Enum.TryParse<GyroButtonCombine>(s.GyroButtonCombine, true, out var gbc))
                p.GyroButtonCombine = gbc;
            if (s.GyroButtons is not null)
                p.GyroButtons = s.GyroButtons.Where(b => !string.IsNullOrWhiteSpace(b)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (s.LeftTrackpadSettings is not null)
                p.LeftTrackpadSettings = TrackpadSurfaceSettings.Clone(s.LeftTrackpadSettings);
            if (s.RightTrackpadSettings is not null)
                p.RightTrackpadSettings = TrackpadSurfaceSettings.Clone(s.RightTrackpadSettings);
            if (s.InvertStickX.HasValue) p.InvertStickX = s.InvertStickX.Value;
            if (s.InvertStickY.HasValue) p.InvertStickY = s.InvertStickY.Value;
            if (s.InvertTrackpadX.HasValue) p.InvertTrackpadX = s.InvertTrackpadX.Value;
            if (s.InvertTrackpadY.HasValue) p.InvertTrackpadY = s.InvertTrackpadY.Value;
            if (s.InvertGyroX.HasValue) p.InvertGyroX = s.InvertGyroX.Value;
            if (s.InvertGyroY.HasValue) p.InvertGyroY = s.InvertGyroY.Value;
            p.IsOfficial = false;
            SaveUnlocked();
        }
        Changed?.Invoke();
    }

    public IReadOnlyList<ControllerSlot> GetControllerSlots()
    {
        lock (_lock)
            return _doc.ControllerSlots.Select(CloneSlot).OrderBy(s => s.Order).ToList();
    }

    public ControllerSlot? FindControllerSlot(string deviceKey)
    {
        if (string.IsNullOrWhiteSpace(deviceKey)) return null;
        lock (_lock)
        {
            var s = _doc.ControllerSlots.FirstOrDefault(x =>
                x.DeviceKey.Equals(deviceKey, StringComparison.OrdinalIgnoreCase));
            return s is null ? null : CloneSlot(s);
        }
    }

    /// <summary>
    /// Upsert connected pad metadata; restores order/override from store when known.
    /// New pads append at the end of the order list.
    /// </summary>
    public ControllerSlot EnsureControllerSlot(string deviceKey, string? displayName, string? model, string? driverId = null)
    {
        if (string.IsNullOrWhiteSpace(deviceKey))
            throw new ArgumentException("deviceKey required");

        lock (_lock)
        {
            var slot = _doc.ControllerSlots.FirstOrDefault(s =>
                s.DeviceKey.Equals(deviceKey, StringComparison.OrdinalIgnoreCase));
            var dirty = false;
            var resolvedDriverId = string.IsNullOrWhiteSpace(driverId)
                ? DriverIds.SteamController
                : driverId!;
            if (slot is null)
            {
                var nextOrder = _doc.ControllerSlots.Count == 0
                    ? 0
                    : _doc.ControllerSlots.Max(s => s.Order) + 1;
                slot = new ControllerSlot
                {
                    DeviceKey = deviceKey,
                    DriverId = resolvedDriverId,
                    Order = nextOrder,
                    Enabled = true
                };
                _doc.ControllerSlots.Add(slot);
                dirty = true;
            }

            if (!string.IsNullOrWhiteSpace(displayName)
                && string.IsNullOrWhiteSpace(slot.DisplayName))
            {
                slot.DisplayName = displayName;
                dirty = true;
            }
            if (!string.IsNullOrWhiteSpace(model)
                && !string.Equals(slot.LastModel, model, StringComparison.OrdinalIgnoreCase))
            {
                slot.LastModel = model;
                dirty = true;
            }
            if (string.IsNullOrEmpty(slot.DriverId))
            {
                slot.DriverId = resolvedDriverId;
                dirty = true;
            }
            else if (!string.IsNullOrWhiteSpace(driverId)
                     && !string.Equals(slot.DriverId, driverId, StringComparison.OrdinalIgnoreCase))
            {
                slot.DriverId = driverId!;
                dirty = true;
            }

            if (dirty)
                SaveUnlocked();
            return CloneSlot(slot);
        }
    }

    public void SetControllerSlotOrder(List<ControllerSlot> slots)
    {
        lock (_lock)
        {
            var byKey = _doc.ControllerSlots.ToDictionary(
                s => string.IsNullOrEmpty(s.DeviceKey) ? s.DriverId + "#" + s.Order : s.DeviceKey,
                s => s,
                StringComparer.OrdinalIgnoreCase);

            var rewritten = new List<ControllerSlot>();
            for (var i = 0; i < slots.Count; i++)
            {
                var incoming = slots[i];
                var key = !string.IsNullOrEmpty(incoming.DeviceKey)
                    ? incoming.DeviceKey
                    : incoming.DriverId;
                if (string.IsNullOrEmpty(key)) continue;

                if (!byKey.TryGetValue(key, out var existing)
                    && !string.IsNullOrEmpty(incoming.DeviceKey))
                {
                    existing = _doc.ControllerSlots.FirstOrDefault(s =>
                        s.DeviceKey.Equals(incoming.DeviceKey, StringComparison.OrdinalIgnoreCase));
                }

                rewritten.Add(new ControllerSlot
                {
                    Order = i,
                    DeviceKey = incoming.DeviceKey ?? existing?.DeviceKey ?? "",
                    DriverId = string.IsNullOrEmpty(incoming.DriverId)
                        ? (existing?.DriverId ?? DriverIds.SteamController)
                        : incoming.DriverId,
                    ProfileId = incoming.ProfileId ?? existing?.ProfileId,
                    DisplayName = incoming.DisplayName ?? existing?.DisplayName,
                    LastModel = incoming.LastModel ?? existing?.LastModel,
                    Enabled = incoming.Enabled,
                    // Reorder payloads from the widget omit rumble — keep the stored value.
                    RumbleEnabled = existing?.RumbleEnabled ?? incoming.RumbleEnabled
                });
            }

            // Keep disconnected remembered slots not in the reorder payload, appended after.
            var keptKeys = new HashSet<string>(
                rewritten.Select(s => s.DeviceKey),
                StringComparer.OrdinalIgnoreCase);
            foreach (var orphan in _doc.ControllerSlots
                         .Where(s => !string.IsNullOrEmpty(s.DeviceKey) && !keptKeys.Contains(s.DeviceKey))
                         .OrderBy(s => s.Order))
            {
                orphan.Order = rewritten.Count;
                rewritten.Add(CloneSlot(orphan));
            }

            _doc.ControllerSlots = rewritten;
            SaveUnlocked();
        }
        Changed?.Invoke();
    }

    public void SetControllerSlotProfile(string deviceKey, string? profileId)
    {
        if (string.IsNullOrWhiteSpace(deviceKey))
            throw new ArgumentException("deviceKey required");

        lock (_lock)
        {
            var slot = _doc.ControllerSlots.FirstOrDefault(s =>
                s.DeviceKey.Equals(deviceKey, StringComparison.OrdinalIgnoreCase));
            if (slot is null)
            {
                slot = new ControllerSlot
                {
                    Order = _doc.ControllerSlots.Count,
                    DeviceKey = deviceKey,
                    DriverId = DriverIds.SteamController
                };
                _doc.ControllerSlots.Add(slot);
            }

            if (profileId is not null
                && _doc.Profiles.All(p => p.Id != profileId))
                throw new ArgumentException("Unknown profile id");

            slot.ProfileId = profileId;
            SaveUnlocked();
        }
        Changed?.Invoke();
    }

    public void SetControllerSlotDisplayName(string deviceKey, string? displayName)
    {
        if (string.IsNullOrWhiteSpace(deviceKey))
            throw new ArgumentException("deviceKey required");

        lock (_lock)
        {
            var slot = _doc.ControllerSlots.FirstOrDefault(s =>
                s.DeviceKey.Equals(deviceKey, StringComparison.OrdinalIgnoreCase));
            if (slot is null)
            {
                slot = new ControllerSlot
                {
                    Order = _doc.ControllerSlots.Count,
                    DeviceKey = deviceKey,
                    DriverId = DriverIds.SteamController
                };
                _doc.ControllerSlots.Add(slot);
            }

            var trimmed = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
            if (trimmed is { Length: > 48 })
                trimmed = trimmed[..48];
            slot.DisplayName = trimmed;
            SaveUnlocked();
        }
        Changed?.Invoke();
    }

    public void SetControllerRumbleEnabled(string deviceKey, bool rumbleEnabled)
    {
        if (string.IsNullOrWhiteSpace(deviceKey))
            throw new ArgumentException("deviceKey required");

        lock (_lock)
        {
            var slot = _doc.ControllerSlots.FirstOrDefault(s =>
                s.DeviceKey.Equals(deviceKey, StringComparison.OrdinalIgnoreCase));
            if (slot is null)
            {
                slot = new ControllerSlot
                {
                    Order = _doc.ControllerSlots.Count,
                    DeviceKey = deviceKey,
                    DriverId = DriverIds.SteamController
                };
                _doc.ControllerSlots.Add(slot);
            }
            if (slot.RumbleEnabled == rumbleEnabled)
                return;
            slot.RumbleEnabled = rumbleEnabled;
            SaveUnlocked();
        }
        Changed?.Invoke();
    }

    /// <summary>Legacy API — prefers DeviceKey when provided via driverId field misuse; prefer SetControllerSlotProfile(deviceKey).</summary>
    public void SetControllerSlotProfileByDriver(string driverId, string? profileId)
    {
        lock (_lock)
        {
            var slot = _doc.ControllerSlots.FirstOrDefault(s =>
                s.DriverId.Equals(driverId, StringComparison.OrdinalIgnoreCase));
            if (slot is null)
            {
                slot = new ControllerSlot
                {
                    Order = _doc.ControllerSlots.Count,
                    DriverId = driverId,
                    DeviceKey = driverId
                };
                _doc.ControllerSlots.Add(slot);
            }
            slot.ProfileId = profileId;
            SaveUnlocked();
        }
        Changed?.Invoke();
    }

    static ControllerSlot CloneSlot(ControllerSlot s) => new()
    {
        Order = s.Order,
        DeviceKey = s.DeviceKey,
        DriverId = s.DriverId,
        ProfileId = s.ProfileId,
        DisplayName = s.DisplayName,
        LastModel = s.LastModel,
        Enabled = s.Enabled,
        RumbleEnabled = s.RumbleEnabled
    };

    public void BindToGame(string profileId, string matchExe, string? matchPathContains = null, string? displayName = null)
    {
        if (string.IsNullOrWhiteSpace(matchExe))
            throw new ArgumentException("matchExe required");

        lock (_lock)
        {
            if (_doc.Profiles.All(p => p.Id != profileId))
                throw new ArgumentException("Unknown profile id");

            var exe = Path.GetFileName(matchExe);
            _doc.ProfileBindings.RemoveAll(b =>
                b.MatchExe.Equals(exe, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(b.MatchPathContains ?? "", matchPathContains ?? "", StringComparison.OrdinalIgnoreCase));

            _doc.ProfileBindings.Add(new ProfileBinding
            {
                ProfileId = profileId,
                MatchExe = exe,
                MatchPathContains = string.IsNullOrWhiteSpace(matchPathContains) ? null : matchPathContains,
                DisplayName = displayName
            });
            SaveUnlocked();
        }
        Changed?.Invoke();
    }

    /// <summary>
    /// Resolves which profile should be active for the foreground process.
    /// PathContains matches beat exe-only matches.
    /// </summary>
    public (ControllerProfile Profile, ActiveProfileSource Source) ResolveForGame(string exe, string path)
    {
        lock (_lock)
        {
            ProfileBinding? bestPath = null;
            ProfileBinding? bestExe = null;
            foreach (var b in _doc.ProfileBindings)
            {
                if (!string.IsNullOrEmpty(b.MatchPathContains) &&
                    !string.IsNullOrEmpty(path) &&
                    path.Contains(b.MatchPathContains, StringComparison.OrdinalIgnoreCase) &&
                    (string.IsNullOrEmpty(b.MatchExe) || b.MatchExe.Equals(exe, StringComparison.OrdinalIgnoreCase) ||
                     Path.GetFileName(path).Equals(b.MatchExe, StringComparison.OrdinalIgnoreCase)))
                {
                    bestPath = b;
                    break;
                }
                if (b.MatchExe.Equals(exe, StringComparison.OrdinalIgnoreCase))
                    bestExe ??= b;
            }

            var hit = bestPath ?? bestExe;
            if (hit is not null)
            {
                var profile = _doc.Profiles.FirstOrDefault(p => p.Id == hit.ProfileId);
                if (profile is not null)
                    return (CloneProfile(profile), ActiveProfileSource.GameRule);
            }

            var active = _doc.Profiles.FirstOrDefault(p => p.Id == _doc.ActiveProfileId) ?? _doc.Profiles.First();
            return (CloneProfile(active), ActiveProfileSource.Manual);
        }
    }

    ProfileStoreDocument LoadOrCreate()
    {
        if (File.Exists(_path))
        {
            try
            {
                var json = File.ReadAllText(_path);
                var doc = JsonSerializer.Deserialize<ProfileStoreDocument>(json, IpcProtocol.JsonOptions);
                if (doc is not null && doc.Profiles.Count > 0)
                {
                    foreach (var p in doc.Profiles)
                        p.MigrateLegacyButtonMap();
                    doc.ProfileBindings ??= [];
                    if (string.IsNullOrEmpty(doc.ActiveProfileId))
                        doc.ActiveProfileId = doc.Profiles[0].Id;
                    _doc = doc;
                    NormalizeUserProfilesUnlocked();
                    SaveUnlocked();
                    return _doc;
                }
            }
            catch
            {
                // fall through to defaults
            }
        }

        // Single editable working layout; official templates live in OfficialLayouts.All only.
        var working = OfficialLayouts.CreateGamepad();
        working.IsOfficial = false;
        working.Name = "My Layout";
        var defaults = new ProfileStoreDocument
        {
            Profiles = [working],
            ActiveProfileId = working.Id
        };
        _doc = defaults;
        SaveUnlocked();
        return defaults;
    }

    /// <summary>
    /// Official templates are catalog-only. Demote/remove IsOfficial rows so Edit only
    /// sees user layouts.
    /// </summary>
    void NormalizeUserProfilesUnlocked()
    {
        foreach (var p in _doc.Profiles)
        {
            if (!string.IsNullOrEmpty(p.LayoutId)) continue;
            if (p.Name.Contains("Desktop", StringComparison.OrdinalIgnoreCase))
                p.LayoutId = OfficialLayouts.Desktop;
            else if (p.Name.Contains("Default Xbox", StringComparison.OrdinalIgnoreCase) ||
                     p.Name.Equals("Gamepad", StringComparison.OrdinalIgnoreCase))
                p.LayoutId = OfficialLayouts.Gamepad;
        }

        var userProfiles = _doc.Profiles.Where(p => !p.IsOfficial).ToList();
        if (userProfiles.Count == 0)
        {
            // Promote the active (or first) official profile into a user working layout.
            var active = _doc.Profiles.FirstOrDefault(p => p.Id == _doc.ActiveProfileId)
                         ?? _doc.Profiles.First();
            active.IsOfficial = false;
            if (string.IsNullOrWhiteSpace(active.Name) ||
                OfficialLayouts.All.Any(l => l.Name.Equals(active.Name, StringComparison.OrdinalIgnoreCase)))
                active.Name = "My Layout";
            userProfiles = [active];
        }

        // Drop catalog-only official rows; keep bindings that still point at user profiles.
        var keepIds = new HashSet<string>(userProfiles.Select(p => p.Id), StringComparer.Ordinal);
        _doc.Profiles = userProfiles;
        _doc.ProfileBindings.RemoveAll(b => !keepIds.Contains(b.ProfileId));
        if (!_doc.Profiles.Any(p => p.Id == _doc.ActiveProfileId))
            _doc.ActiveProfileId = _doc.Profiles[0].Id;
    }

    void SaveUnlocked()
    {
        foreach (var p in _doc.Profiles)
            p.MigrateLegacyButtonMap();
        var json = JsonSerializer.Serialize(_doc, IpcProtocol.JsonOptions);
        File.WriteAllText(_path, json);
    }

    static ProfileStoreDocument Clone(ProfileStoreDocument d) => new()
    {
        ActiveProfileId = d.ActiveProfileId,
        BridgeEnabled = d.BridgeEnabled,
        AutoPauseWhenSteamRunning = d.AutoPauseWhenSteamRunning,
        StartWithWindows = d.StartWithWindows,
        Profiles = d.Profiles.Select(CloneProfile).ToList(),
        ProfileBindings = d.ProfileBindings.Select(CloneBinding).ToList(),
        ControllerSlots = d.ControllerSlots.Select(CloneSlot).ToList()
    };

    static ProfileBinding CloneBinding(ProfileBinding b) => new()
    {
        ProfileId = b.ProfileId,
        MatchExe = b.MatchExe,
        MatchPathContains = b.MatchPathContains,
        DisplayName = b.DisplayName
    };

    static ControllerProfile CloneProfile(ControllerProfile p)
    {
        p.MigrateLegacyButtonMap();
        var clone = new ControllerProfile
        {
            Id = p.Id,
            Name = p.Name,
            DriverId = p.DriverId,
            LayoutId = p.LayoutId,
            IsOfficial = p.IsOfficial,
            InputMap = p.InputMap.ToDictionary(
                kv => kv.Key,
                kv => CloneAction(kv.Value),
                StringComparer.OrdinalIgnoreCase),
            LeftTrackpad = p.LeftTrackpad,
            RightTrackpad = p.RightTrackpad,
            LeftTrackpadSettings = TrackpadSurfaceSettings.Clone(p.LeftTrackpadSettings),
            RightTrackpadSettings = TrackpadSurfaceSettings.Clone(p.RightTrackpadSettings),
            Gyro = p.Gyro,
            GyroSensitivity = p.GyroSensitivity,
            GyroButtons = p.GyroButtons.ToList(),
            GyroButtonMode = p.GyroButtonMode,
            GyroButtonCombine = p.GyroButtonCombine,
            GyroDotsPer360 = p.GyroDotsPer360,
            StickDeadzone = p.StickDeadzone,
            TriggerDeadzone = p.TriggerDeadzone,
            TrackpadDeadzone = p.TrackpadDeadzone,
            StickSensitivityX = p.StickSensitivityX,
            StickSensitivityY = p.StickSensitivityY,
            TrackpadSensitivityX = p.TrackpadSensitivityX,
            TrackpadSensitivityY = p.TrackpadSensitivityY,
            GyroSensitivityX = p.GyroSensitivityX,
            GyroSensitivityY = p.GyroSensitivityY,
            InvertStickX = p.InvertStickX,
            InvertStickY = p.InvertStickY,
            InvertTrackpadX = p.InvertTrackpadX,
            InvertTrackpadY = p.InvertTrackpadY,
            InvertGyroX = p.InvertGyroX,
            InvertGyroY = p.InvertGyroY
        };
        clone.EnsureLockedMappings();
        return clone;
    }

    static OutputAction CloneAction(OutputAction a) => new()
    {
        Kind = a.Kind,
        Xbox = a.Xbox,
        VirtualKey = a.VirtualKey,
        Modifiers = a.Modifiers,
        MouseButton = a.MouseButton
    };
}
