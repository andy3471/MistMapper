using MistMapper.Shared;

namespace MistMapper.Host.Services;

/// <summary>
/// Shared remap/profile mutations for named-pipe and Game Bar IPC fronts.
/// </summary>
public sealed class HostCommandService
{
    readonly ProfileService _profiles;
    readonly BridgeService _bridge;

    public HostCommandService(ProfileService profiles, BridgeService bridge)
    {
        _profiles = profiles;
        _bridge = bridge;
    }

    public void SetBinding(string profileId, string inputId, ActivatorType activator, int slot, OutputAction action)
    {
        EnsureNotLockedGuide(inputId);
        profileId = _bridge.ResolveRemapTargetProfileId(profileId);
        _profiles.RemapBindingAction(profileId, inputId, activator, slot, action);
    }

    public void RemapAction(string profileId, string inputId, OutputAction action)
    {
        EnsureNotLockedGuide(inputId);
        profileId = _bridge.ResolveRemapTargetProfileId(profileId);
        _profiles.RemapAction(profileId, inputId, action);
    }

    public void RemapButton(string profileId, PhysicalInput physical, XboxOutput xbox)
    {
        if (physical == PhysicalInput.Steam)
            throw new InvalidOperationException("Steam/Guide is locked to Xbox Guide.");
        profileId = _bridge.ResolveRemapTargetProfileId(profileId);
        _profiles.Remap(profileId, physical, xbox);
    }

    public void SetTrackpadMode(string profileId, bool left, TrackpadMode mode)
    {
        profileId = _bridge.ResolveRemapTargetProfileId(profileId);
        _profiles.SetTrackpad(profileId, left, mode);
    }

    public void SetGyroMode(string profileId, GyroMode mode, float? sensitivity = null)
    {
        profileId = _bridge.ResolveRemapTargetProfileId(profileId);
        _profiles.SetGyro(profileId, mode, sensitivity);
    }

    public void SetSensitivity(string profileId, SensitivityPayload payload)
    {
        profileId = _bridge.ResolveRemapTargetProfileId(profileId);
        _profiles.SetSensitivity(profileId, payload);
    }

    public static OutputAction ParseAction(string[] bits)
    {
        var kind = bits.Length > 2 ? bits[2] : "none";
        var value = bits.Length > 3 ? bits[3] : "";
        var mods = bits.Length > 4 && int.TryParse(bits[4], out var m) ? (KeyModifiers)m : KeyModifiers.None;

        return kind.ToLowerInvariant() switch
        {
            "xbox" when Enum.TryParse<XboxOutput>(value, true, out var xbox) => OutputAction.FromXbox(xbox),
            "key" when int.TryParse(value, out var vk) => OutputAction.FromKey(vk, mods),
            "mouse" when Enum.TryParse<MouseButtonOutput>(value, true, out var mb) => OutputAction.FromMouse(mb),
            _ => OutputAction.None()
        };
    }

    static void EnsureNotLockedGuide(string inputId)
    {
        if (MappingLocks.IsLockedGuideInput(inputId))
            throw new InvalidOperationException("Steam/Guide is locked to Xbox Guide.");
    }
}
