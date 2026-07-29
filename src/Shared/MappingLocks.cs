namespace MistMapper.Shared;

/// <summary>Inputs that cannot be remapped by the user.</summary>
public static class MappingLocks
{
    /// <summary>Physical Steam / Guide button is always Xbox Guide.</summary>
    public const string SteamInputId = "Steam";

    public static bool IsLockedGuideInput(string? inputId) =>
        !string.IsNullOrEmpty(inputId) &&
        (inputId.Equals(SteamInputId, StringComparison.OrdinalIgnoreCase) ||
         inputId.Equals("Guide", StringComparison.OrdinalIgnoreCase));

    public static OutputAction LockedGuideAction { get; } = OutputAction.FromXbox(XboxOutput.Guide);
}
