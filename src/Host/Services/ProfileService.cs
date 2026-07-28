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
                return _doc.Profiles.FirstOrDefault(p => p.Id == _doc.ActiveProfileId)
                       ?? _doc.Profiles.First();
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
            if (_doc.ActiveProfileId == id)
                _doc.ActiveProfileId = _doc.Profiles[0].Id;
            SaveUnlocked();
        }
        Changed?.Invoke();
    }

    public void Remap(string profileId, PhysicalInput physical, XboxOutput xbox)
    {
        lock (_lock)
        {
            var p = _doc.Profiles.First(x => x.Id == profileId);
            p.SetButton(physical, xbox);
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
            SaveUnlocked();
        }
        Changed?.Invoke();
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
                    if (string.IsNullOrEmpty(doc.ActiveProfileId))
                        doc.ActiveProfileId = doc.Profiles[0].Id;
                    return doc;
                }
            }
            catch
            {
                // fall through to defaults
            }
        }

        var defaults = new ProfileStoreDocument();
        var xbox = ControllerProfile.CreateDefault();
        var desktop = ControllerProfile.CreateDesktop();
        defaults.Profiles.Add(xbox);
        defaults.Profiles.Add(desktop);
        defaults.ActiveProfileId = xbox.Id;
        _doc = defaults;
        SaveUnlocked();
        return defaults;
    }

    void SaveUnlocked()
    {
        var json = JsonSerializer.Serialize(_doc, IpcProtocol.JsonOptions);
        File.WriteAllText(_path, json);
    }

    static ProfileStoreDocument Clone(ProfileStoreDocument d) => new()
    {
        ActiveProfileId = d.ActiveProfileId,
        BridgeEnabled = d.BridgeEnabled,
        AutoPauseWhenSteamRunning = d.AutoPauseWhenSteamRunning,
        StartWithWindows = d.StartWithWindows,
        Profiles = d.Profiles.Select(CloneProfile).ToList()
    };

    static ControllerProfile CloneProfile(ControllerProfile p) => new()
    {
        Id = p.Id,
        Name = p.Name,
        ButtonMap = new Dictionary<string, string>(p.ButtonMap, StringComparer.OrdinalIgnoreCase),
        LeftTrackpad = p.LeftTrackpad,
        RightTrackpad = p.RightTrackpad,
        Gyro = p.Gyro,
        GyroSensitivity = p.GyroSensitivity,
        StickDeadzone = p.StickDeadzone,
        TriggerDeadzone = p.TriggerDeadzone
    };
}
