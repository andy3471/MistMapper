using System.Text.Json;
using SteamControllerBridge.Shared;

namespace SteamControllerBridge.Host.Services;

public sealed class ProfileService
{
    readonly string _path;
    readonly object _lock = new();
    ProfileStoreDocument _doc;

    public event Action? Changed;

    public ProfileService(string? directory = null)
    {
        var dir = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SteamControllerBridge");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "profiles.json");
        _doc = LoadOrCreate();
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
    }

    public IReadOnlyList<ControllerProfile> GetProfiles()
    {
        lock (_lock) return _doc.Profiles.Select(CloneProfile).ToList();
    }

    public IReadOnlyList<ProfileBinding> GetBindings()
    {
        lock (_lock) return _doc.ProfileBindings.Select(CloneBinding).ToList();
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

    /// <summary>Ensures each official layout id exists at least once (does not overwrite user edits).</summary>
    public void EnsureOfficialLayouts()
    {
        lock (_lock)
        {
            var existing = new HashSet<string>(
                _doc.Profiles.Where(p => !string.IsNullOrEmpty(p.LayoutId)).Select(p => p.LayoutId!),
                StringComparer.OrdinalIgnoreCase);

            bool added = false;
            foreach (var info in OfficialLayouts.All)
            {
                if (existing.Contains(info.Id)) continue;
                var profile = OfficialLayouts.Create(info.Id);
                profile.IsOfficial = true;
                _doc.Profiles.Add(profile);
                added = true;
            }

            if (added)
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
                    EnsureOfficialLayoutsUnlocked();
                    SaveUnlocked();
                    return _doc;
                }
            }
            catch
            {
                // fall through to defaults
            }
        }

        var defaults = new ProfileStoreDocument();
        foreach (var info in OfficialLayouts.All)
        {
            var p = OfficialLayouts.Create(info.Id);
            p.IsOfficial = true;
            defaults.Profiles.Add(p);
        }
        defaults.ActiveProfileId = defaults.Profiles[0].Id;
        _doc = defaults;
        SaveUnlocked();
        return defaults;
    }

    void EnsureOfficialLayoutsUnlocked()
    {
        var existing = new HashSet<string>(
            _doc.Profiles.Where(p => !string.IsNullOrEmpty(p.LayoutId)).Select(p => p.LayoutId!),
            StringComparer.OrdinalIgnoreCase);

        // Migrate legacy names into layout ids when missing
        foreach (var p in _doc.Profiles)
        {
            if (!string.IsNullOrEmpty(p.LayoutId)) continue;
            if (p.Name.Contains("Desktop", StringComparison.OrdinalIgnoreCase))
                p.LayoutId = OfficialLayouts.Desktop;
            else if (p.Name.Contains("Default Xbox", StringComparison.OrdinalIgnoreCase) ||
                     p.Name.Equals("Gamepad", StringComparison.OrdinalIgnoreCase))
                p.LayoutId = OfficialLayouts.Gamepad;
        }

        existing = new HashSet<string>(
            _doc.Profiles.Where(p => !string.IsNullOrEmpty(p.LayoutId)).Select(p => p.LayoutId!),
            StringComparer.OrdinalIgnoreCase);

        foreach (var info in OfficialLayouts.All)
        {
            if (existing.Contains(info.Id)) continue;
            var profile = OfficialLayouts.Create(info.Id);
            profile.IsOfficial = true;
            _doc.Profiles.Add(profile);
        }
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
        ProfileBindings = d.ProfileBindings.Select(CloneBinding).ToList()
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
            Gyro = p.Gyro,
            GyroSensitivity = p.GyroSensitivity,
            StickDeadzone = p.StickDeadzone,
            TriggerDeadzone = p.TriggerDeadzone
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
