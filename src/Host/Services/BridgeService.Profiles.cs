using MistMapper.Shared;

namespace MistMapper.Host.Services;

public sealed partial class BridgeService
{
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
                    ?? DisplayNameForModel(slotMeta?.LastModel ?? "sc2");
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
            return ResolveProfileForSlot(slot.DeviceKey, slot.ProfileId).profile;

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

        if (slot is not null && !string.IsNullOrWhiteSpace(slot.ProfileId))
            return slot.ProfileId!;

        return EnsureLayoutForCurrentGame(sourceProfileId);
    }

    public bool SelectedPadHasProfileOverride()
    {
        string key;
        lock (_gate) key = _selectedDeviceKey;
        lock (_gate)
        {
            var slot = FindSlotUnlocked(key) ?? PrimarySlotUnlocked();
            return slot is not null && !string.IsNullOrWhiteSpace(slot.ProfileId);
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
        string? profileId)
    {
        if (!string.IsNullOrWhiteSpace(profileId))
        {
            var overrideProfile = _profiles.GetProfiles()
                .FirstOrDefault(p => p.Id.Equals(profileId, StringComparison.OrdinalIgnoreCase));
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
                slot.RumbleEnabled = stored.RumbleEnabled;
                slot.ProfileId = stored.ProfileId;
                if (!string.IsNullOrWhiteSpace(stored.DisplayName))
                    slot.DisplayName = stored.DisplayName!;
            }
            _slots.Sort((a, b) => a.Order.CompareTo(b.Order));
        }
    }
}
