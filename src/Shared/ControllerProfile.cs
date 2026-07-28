using System.Text.Json.Serialization;

namespace SteamControllerBridge.Shared;

public sealed class ControllerProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Default";
    public Dictionary<string, string> ButtonMap { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public TrackpadMode LeftTrackpad { get; set; } = TrackpadMode.Off;
    public TrackpadMode RightTrackpad { get; set; } = TrackpadMode.Off;
    public GyroMode Gyro { get; set; } = GyroMode.Off;
    public float GyroSensitivity { get; set; } = 1.0f;
    public float StickDeadzone { get; set; } = 0.08f;
    public float TriggerDeadzone { get; set; } = 0.05f;

    public static ControllerProfile CreateDefault()
    {
        var p = new ControllerProfile { Name = "Default Xbox" };
        foreach (var (src, dst) in DefaultButtonPairs)
            p.ButtonMap[src.ToString()] = dst.ToString();
        return p;
    }

    public static ControllerProfile CreateDesktop()
    {
        var p = CreateDefault();
        p.Id = Guid.NewGuid().ToString("N");
        p.Name = "Desktop (right pad mouse)";
        p.RightTrackpad = TrackpadMode.AsMouse;
        return p;
    }

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

    public XboxOutput MapButton(PhysicalInput input)
    {
        if (ButtonMap.TryGetValue(input.ToString(), out var name) &&
            Enum.TryParse<XboxOutput>(name, true, out var output))
            return output;
        return XboxOutput.None;
    }

    public void SetButton(PhysicalInput input, XboxOutput output)
    {
        if (output == XboxOutput.None)
            ButtonMap.Remove(input.ToString());
        else
            ButtonMap[input.ToString()] = output.ToString();
    }
}

public sealed class ProfileStoreDocument
{
    public string ActiveProfileId { get; set; } = "";
    public List<ControllerProfile> Profiles { get; set; } = [];
    public bool BridgeEnabled { get; set; } = true;
    public bool AutoPauseWhenSteamRunning { get; set; } = true;
    public bool StartWithWindows { get; set; } = true;
}
