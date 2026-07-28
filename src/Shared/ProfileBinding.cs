namespace SteamControllerBridge.Shared;

public sealed class ProfileBinding
{
    public string ProfileId { get; set; } = "";
    public string MatchExe { get; set; } = "";
    public string? MatchPathContains { get; set; }
    public string? DisplayName { get; set; }
}

public enum ActiveProfileSource
{
    Manual,
    GameRule,
    Default
}
