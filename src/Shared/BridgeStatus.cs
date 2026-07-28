namespace SteamControllerBridge.Shared;

public enum BridgeRunState
{
    Stopped,
    WaitingForController,
    WaitingForSession,
    LizardMode,
    Bridging,
    PausedSteam,
    PausedLocked,
    Error
}

public sealed class BridgeStatus
{
    public BridgeRunState State { get; set; } = BridgeRunState.Stopped;
    public bool BridgeEnabled { get; set; }
    public bool ControllerConnected { get; set; }
    public bool SteamRunning { get; set; }
    public bool SessionLocked { get; set; }
    public bool ViiperConnected { get; set; }
    public string ActiveProfileId { get; set; } = "";
    public string ActiveProfileName { get; set; } = "";
    public string Message { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
