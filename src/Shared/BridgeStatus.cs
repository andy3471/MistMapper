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

public sealed class DependencyStatus
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public bool Ok { get; set; }
    public string Detail { get; set; } = "";
}

public sealed class BridgeStatus
{
    public BridgeRunState State { get; set; } = BridgeRunState.Stopped;
    public bool BridgeEnabled { get; set; }
    public bool ControllerConnected { get; set; }
    public bool SteamRunning { get; set; }
    public bool SessionLocked { get; set; }
    public bool ViiperConnected { get; set; }
    /// <summary>True while Game Bar overlay is open and runtime uses fixed Gamepad mapping.</summary>
    public bool GameBarOverrideActive { get; set; }
    public string ActiveProfileId { get; set; } = "";
    public string ActiveProfileName { get; set; } = "";
    public string ActiveProfileSource { get; set; } = "Default";
    public string ActiveDriverId { get; set; } = "";
    public string ActiveDriverName { get; set; } = "";
    public string CurrentGameExe { get; set; } = "";
    public string CurrentGamePath { get; set; } = "";
    public List<string> PressedInputs { get; set; } = [];
    public List<DependencyStatus> Dependencies { get; set; } = [];
    public string Message { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
