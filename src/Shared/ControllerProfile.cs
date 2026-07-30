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

    /// <summary>Input id → activator bindings (Regular / LongPress, up to 2 actions each).</summary>
    public Dictionary<string, List<InputBinding>> Bindings { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Legacy 1:1 map; migrated into <see cref="Bindings"/> on load.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, OutputAction>? InputMap { get; set; }

    /// <summary>Legacy Xbox-only map; migrated into <see cref="InputMap"/> then <see cref="Bindings"/> on load.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? ButtonMap { get; set; }

    /// <summary>Hold duration before LongPress activators fire (ms).</summary>
    public int LongPressMs { get; set; } = 400;

    public TrackpadMode LeftTrackpad { get; set; } = TrackpadMode.Off;
    public TrackpadMode RightTrackpad { get; set; } = TrackpadMode.Off;
    public TrackpadSurfaceSettings LeftTrackpadSettings { get; set; } = new();
    public TrackpadSurfaceSettings RightTrackpadSettings { get; set; } = new();
    public GyroMode Gyro { get; set; } = GyroMode.Off;
    public float GyroSensitivity { get; set; } = 1.0f;

    /// <summary>
    /// Inputs that enable/suppress/toggle gyro (PhysicalInput ids).
    /// Empty = gyro always on when mode ≠ Off (Steam default).
    /// </summary>
    public List<string> GyroButtons { get; set; } = [];

    public GyroButtonMode GyroButtonMode { get; set; } = GyroButtonMode.HoldToEnable;
    public GyroButtonCombine GyroButtonCombine { get; set; } = GyroButtonCombine.Any;

    /// <summary>Mouse pixels for one full 360° turn at 1× sensitivity (Steam natural angles).</summary>
    public float GyroDotsPer360 { get; set; } = 6545f;

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
            p.SetAction(src.ToString(), OutputAction.FromXbox(dst));
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

        InputMap ??= new Dictionary<string, OutputAction>(StringComparer.OrdinalIgnoreCase);

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

        if (InputMap.Comparer != StringComparer.OrdinalIgnoreCase)
            InputMap = new Dictionary<string, OutputAction>(InputMap, StringComparer.OrdinalIgnoreCase);

        if (Bindings.Comparer != StringComparer.OrdinalIgnoreCase)
            Bindings = new Dictionary<string, List<InputBinding>>(Bindings, StringComparer.OrdinalIgnoreCase);

        MigrateInputMapToBindings();
        EnsureLockedMappings();

        if (LongPressMs < 50)
            LongPressMs = 400;
    }

    /// <summary>Fold legacy <see cref="InputMap"/> entries into <see cref="Bindings"/> then clear InputMap.</summary>
    public void MigrateInputMapToBindings()
    {
        if (InputMap is { Count: > 0 })
        {
            foreach (var (key, action) in InputMap)
            {
                if (action is null || action.Kind == OutputActionKind.None) continue;
                if (Bindings.ContainsKey(key)) continue;
                Bindings[key] = [InputBinding.FromAction(action)];
            }
            InputMap = null;
        }
    }

    public IReadOnlyList<InputBinding> GetBindings(string inputId)
    {
        if (MappingLocks.IsLockedGuideInput(inputId))
            return [InputBinding.FromAction(MappingLocks.LockedGuideAction)];

        if (Bindings.TryGetValue(inputId, out var list) && list is { Count: > 0 })
            return list;
        return [];
    }

    public InputBinding GetOrCreateBinding(string inputId, ActivatorType activator)
    {
        if (MappingLocks.IsLockedGuideInput(inputId))
            return InputBinding.FromAction(MappingLocks.LockedGuideAction);

        if (!Bindings.TryGetValue(inputId, out var list) || list is null)
        {
            list = [];
            Bindings[inputId] = list;
        }

        var existing = list.FirstOrDefault(b => b.Activator == activator);
        if (existing is not null) return existing;

        var created = new InputBinding { Activator = activator };
        list.Add(created);
        return created;
    }

    /// <summary>First Regular action (display / stick / trigger compatibility).</summary>
    public OutputAction GetAction(string inputId)
    {
        if (MappingLocks.IsLockedGuideInput(inputId))
            return MappingLocks.LockedGuideAction;

        foreach (var binding in GetBindings(inputId))
        {
            if (binding.Activator != ActivatorType.Regular) continue;
            foreach (var a in binding.Actions)
            {
                if (a.Kind != OutputActionKind.None)
                    return a;
            }
        }
        return OutputAction.None();
    }

    public OutputAction GetAction(PhysicalInput input) => GetAction(input.ToString());

    public XboxOutput MapButton(PhysicalInput input)
    {
        var a = GetAction(input);
        return a.Kind == OutputActionKind.Xbox ? a.Xbox : XboxOutput.None;
    }

    /// <summary>Sets Regular binding to a single action (legacy / simple remap API).</summary>
    public void SetAction(string inputId, OutputAction action)
    {
        if (MappingLocks.IsLockedGuideInput(inputId))
        {
            Bindings[MappingLocks.SteamInputId] = [InputBinding.FromAction(MappingLocks.LockedGuideAction)];
            return;
        }

        if (!Bindings.TryGetValue(inputId, out var list) || list is null)
        {
            list = [];
            Bindings[inputId] = list;
        }

        list.RemoveAll(b => b.Activator == ActivatorType.Regular);
        if (action.Kind != OutputActionKind.None)
            list.Insert(0, InputBinding.FromAction(action));

        PruneEmptyBindings(inputId);
    }

    /// <summary>Set one action slot (0 or 1) on an activator. Empty action removes that slot.</summary>
    public void SetBindingAction(string inputId, ActivatorType activator, int slot, OutputAction action)
    {
        if (MappingLocks.IsLockedGuideInput(inputId))
        {
            Bindings[MappingLocks.SteamInputId] = [InputBinding.FromAction(MappingLocks.LockedGuideAction)];
            return;
        }

        slot = Math.Clamp(slot, 0, 1);
        var binding = GetOrCreateBinding(inputId, activator);

        while (binding.Actions.Count <= slot)
            binding.Actions.Add(OutputAction.None());

        if (action.Kind == OutputActionKind.None)
        {
            binding.Actions[slot] = OutputAction.None();
            binding.Actions = binding.Actions.Where(a => a.Kind != OutputActionKind.None).ToList();
        }
        else
        {
            binding.Actions[slot] = action;
            // Keep max 2
            if (binding.Actions.Count > 2)
                binding.Actions = binding.Actions.Take(2).ToList();
        }

        PruneEmptyBindings(inputId);
    }

    void PruneEmptyBindings(string inputId)
    {
        if (!Bindings.TryGetValue(inputId, out var list)) return;
        list.RemoveAll(b => b.Actions.Count == 0 || b.Actions.All(a => a.Kind == OutputActionKind.None));
        if (list.Count == 0)
            Bindings.Remove(inputId);
    }

    /// <summary>Ensures Steam is always mapped to Xbox Guide in persisted profiles.</summary>
    public void EnsureLockedMappings()
    {
        Bindings[MappingLocks.SteamInputId] = [InputBinding.FromAction(MappingLocks.LockedGuideAction)];
    }

    public void SetButton(PhysicalInput input, XboxOutput output) =>
        SetAction(input.ToString(), OutputAction.FromXbox(output));

    public string FormatBindingsDisplay(string inputId)
    {
        var list = GetBindings(inputId);
        if (list.Count == 0) return "None";
        return string.Join(" | ", list.Select(b => b.ToDisplayString()));
    }
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
    /// <summary>When false, game rumble is not forwarded to this pad (Identify still works).</summary>
    public bool RumbleEnabled { get; set; } = true;
}
