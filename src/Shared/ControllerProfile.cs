using System.Text.Json.Serialization;

namespace MistMapper.Shared;

public sealed class ControllerProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Default";
    public string DriverId { get; set; } = DriverIds.SteamController;

    /// <summary>Official template id when created from <see cref="OfficialLayouts"/>.</summary>
    public string? LayoutId { get; set; }

    /// <summary>True for stock official profiles seeded by the app.</summary>
    public bool IsOfficial { get; set; }

    /// <summary>Input id → action. Preferred mapping store.</summary>
    public Dictionary<string, OutputAction> InputMap { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Legacy Xbox-only map; migrated into <see cref="InputMap"/> on load.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? ButtonMap { get; set; }

    public TrackpadMode LeftTrackpad { get; set; } = TrackpadMode.Off;
    public TrackpadMode RightTrackpad { get; set; } = TrackpadMode.Off;
    public GyroMode Gyro { get; set; } = GyroMode.Off;
    public float GyroSensitivity { get; set; } = 1.0f;
    public float StickDeadzone { get; set; } = 0.08f;
    public float TriggerDeadzone { get; set; } = 0.05f;
    public float TrackpadDeadzone { get; set; } = 0.02f;

    public float StickSensitivityX { get; set; } = 1.0f;
    public float StickSensitivityY { get; set; } = 1.0f;
    public float TrackpadSensitivityX { get; set; } = 1.0f;
    public float TrackpadSensitivityY { get; set; } = 1.0f;
    public float GyroSensitivityX { get; set; } = 1.0f;
    public float GyroSensitivityY { get; set; } = 1.0f;

    public bool InvertStickX { get; set; }
    public bool InvertStickY { get; set; }
    public bool InvertTrackpadX { get; set; }
    public bool InvertTrackpadY { get; set; }
    public bool InvertGyroX { get; set; }
    public bool InvertGyroY { get; set; }

    public static ControllerProfile CreateDefault()
    {
        var p = new ControllerProfile
        {
            Name = "Gamepad",
            DriverId = DriverIds.SteamController,
            LayoutId = OfficialLayouts.Gamepad,
            IsOfficial = true
        };
        foreach (var (src, dst) in DefaultButtonPairs)
            p.InputMap[src.ToString()] = OutputAction.FromXbox(dst);
        return p;
    }

    public static ControllerProfile CreateDesktop() => OfficialLayouts.CreateDesktop();

    [JsonIgnore]
    public static readonly (PhysicalInput Src, XboxOutput Dst)[] DefaultButtonPairs =
    [
        (PhysicalInput.A, XboxOutput.A),
        (PhysicalInput.B, XboxOutput.B),
        (PhysicalInput.X, XboxOutput.X),
        (PhysicalInput.Y, XboxOutput.Y),
        (PhysicalInput.Lb, XboxOutput.Lb),
        (PhysicalInput.Rb, XboxOutput.Rb),
        (PhysicalInput.View, XboxOutput.Back),
        (PhysicalInput.Menu, XboxOutput.Start),
        (PhysicalInput.Steam, XboxOutput.Guide),
        (PhysicalInput.LsClick, XboxOutput.LsClick),
        (PhysicalInput.RsClick, XboxOutput.RsClick),
        (PhysicalInput.DpadUp, XboxOutput.DpadUp),
        (PhysicalInput.DpadDown, XboxOutput.DpadDown),
        (PhysicalInput.DpadLeft, XboxOutput.DpadLeft),
        (PhysicalInput.DpadRight, XboxOutput.DpadRight),
        (PhysicalInput.Lt, XboxOutput.Lt),
        (PhysicalInput.Rt, XboxOutput.Rt),
        (PhysicalInput.LeftStick, XboxOutput.LeftStick),
        (PhysicalInput.RightStick, XboxOutput.RightStick),
        (PhysicalInput.L4, XboxOutput.Lb),
        (PhysicalInput.L5, XboxOutput.Back),
        (PhysicalInput.R4, XboxOutput.Rb),
        (PhysicalInput.R5, XboxOutput.Start),
        (PhysicalInput.LeftTrackpadClick, XboxOutput.LsClick),
        (PhysicalInput.RightTrackpadClick, XboxOutput.RsClick),
    ];

    public void MigrateLegacyButtonMap()
    {
        if (string.IsNullOrWhiteSpace(DriverId))
            DriverId = DriverIds.SteamController;

        if (ButtonMap is { Count: > 0 })
        {
            foreach (var (key, value) in ButtonMap)
            {
                if (InputMap.ContainsKey(key)) continue;
                if (Enum.TryParse<XboxOutput>(value, true, out var xbox))
                    InputMap[key] = OutputAction.FromXbox(xbox);
            }
            ButtonMap = null;
        }

        // Normalize dictionary comparer after deserialize
        if (InputMap.Comparer != StringComparer.OrdinalIgnoreCase)
            InputMap = new Dictionary<string, OutputAction>(InputMap, StringComparer.OrdinalIgnoreCase);

        EnsureLockedMappings();
    }

    public OutputAction GetAction(string inputId)
    {
        if (MappingLocks.IsLockedGuideInput(inputId))
            return MappingLocks.LockedGuideAction;

        if (InputMap.TryGetValue(inputId, out var action) && action is not null)
            return action;
        return OutputAction.None();
    }

    public OutputAction GetAction(PhysicalInput input) => GetAction(input.ToString());

    public XboxOutput MapButton(PhysicalInput input)
    {
        var a = GetAction(input);
        return a.Kind == OutputActionKind.Xbox ? a.Xbox : XboxOutput.None;
    }

    public void SetAction(string inputId, OutputAction action)
    {
        if (MappingLocks.IsLockedGuideInput(inputId))
        {
            InputMap[MappingLocks.SteamInputId] = MappingLocks.LockedGuideAction;
            return;
        }

        if (action.Kind == OutputActionKind.None)
            InputMap.Remove(inputId);
        else
            InputMap[inputId] = action;
    }

    /// <summary>Ensures Steam is always mapped to Xbox Guide in persisted profiles.</summary>
    public void EnsureLockedMappings()
    {
        InputMap[MappingLocks.SteamInputId] = MappingLocks.LockedGuideAction;
    }

    public void SetButton(PhysicalInput input, XboxOutput output) =>
        SetAction(input.ToString(), OutputAction.FromXbox(output));
}

public sealed class ProfileStoreDocument
{
    public string ActiveProfileId { get; set; } = "";
    public List<ControllerProfile> Profiles { get; set; } = [];
    public List<ProfileBinding> ProfileBindings { get; set; } = [];
    public List<ControllerSlot> ControllerSlots { get; set; } = [];
    public bool BridgeEnabled { get; set; } = true;
    public bool AutoPauseWhenSteamRunning { get; set; } = true;
    public bool StartWithWindows { get; set; } = true;
}

public sealed class ControllerSlot
{
    public int Order { get; set; }
    /// <summary>Stable HID physical key (or test id). Primary identity for multi-pad.</summary>
    public string DeviceKey { get; set; } = "";
    public string DriverId { get; set; } = DriverIds.SteamController;
    /// <summary>Per-pad profile override; null uses shared default / game binding.</summary>
    public string? ProfileId { get; set; }
    public string? DisplayName { get; set; }
    /// <summary>"sc1" / "sc2" remembered for UI when disconnected.</summary>
    public string? LastModel { get; set; }
    public bool Enabled { get; set; } = true;
}
