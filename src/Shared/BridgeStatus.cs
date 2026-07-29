namespace MistMapper.Shared;

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

public sealed class ControllerStatus
{
    public string DeviceKey { get; set; } = "";
    public string Model { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public int Order { get; set; }
    public bool Enabled { get; set; } = true;
    public bool Connected { get; set; }
    /// <summary>When false, game rumble is not forwarded (Identify still works).</summary>
    public bool RumbleEnabled { get; set; } = true;
    public string? ProfileId { get; set; }
    public string? ProfileName { get; set; }
    /// <summary>True when ProfileId is a per-pad override (not shared default/game).</summary>
    public bool HasProfileOverride { get; set; }
    public List<string> PressedInputs { get; set; } = [];
}

public sealed class BridgeStatus
{
    public BridgeRunState State { get; set; } = BridgeRunState.Stopped;
    public bool BridgeEnabled { get; set; }
    /// <summary>When true, bridge pauses while Steam.exe is running.</summary>
    public bool AutoPauseWhenSteamRunning { get; set; } = true;
    public bool ControllerConnected { get; set; }
    /// <summary>"sc1" (2015), "sc2" (2026), or "" when unknown/disconnected. Selected / primary pad.</summary>
    public string ControllerModel { get; set; } = "";
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
    public string CurrentGameName { get; set; } = "";
    public List<string> PressedInputs { get; set; } = [];
    /// <summary>Connected pads ordered by player index (Order).</summary>
    public List<ControllerStatus> Controllers { get; set; } = [];
    /// <summary>Widget remap / view target.</summary>
    public string SelectedDeviceKey { get; set; } = "";
    public List<DependencyStatus> Dependencies { get; set; } = [];
    public string Message { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
