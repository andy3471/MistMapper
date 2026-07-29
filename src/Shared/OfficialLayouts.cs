namespace MistMapper.Shared;

/// <summary>Steam Input–style official templates users can instantiate as profiles.</summary>
public static class OfficialLayouts
{
    public const string Gamepad = "gamepad";
    public const string Desktop = "desktop";
    public const string MouseJoystick = "mouse-joystick";
    public const string KeyboardMouse = "keyboard-mouse";
    public const string Racing = "racing";

    public sealed class LayoutInfo
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public string Description { get; init; } = "";
    }

    public static IReadOnlyList<LayoutInfo> All { get; } =
    [
        new()
        {
            Id = Gamepad,
            Name = "Gamepad",
            Description = "1:1 Xbox 360 pad — closest to Steam Input Gamepad."
        },
        new()
        {
            Id = Desktop,
            Name = "Desktop",
            Description = "Right trackpad as mouse; paddles for click / Back / Start."
        },
        new()
        {
            Id = MouseJoystick,
            Name = "Mouse Joystick",
            Description = "FPS-style: left stick move; right pad + gyro aim like a mouse but output as the right stick (gamespad-only games)."
        },
        new()
        {
            Id = KeyboardMouse,
            Name = "Keyboard & Mouse",
            Description = "WASD on left stick/dpad, mouse on right pad, common keys on face buttons."
        },
        new()
        {
            Id = Racing,
            Name = "Racing Wheel-ish",
            Description = "Triggers accelerate/brake, stick steer, face buttons for common race actions."
        },
    ];

    public static ControllerProfile Create(string layoutId, string? customName = null)
    {
        var profile = layoutId.ToLowerInvariant() switch
        {
            Desktop => CreateDesktop(),
            MouseJoystick => CreateMouseJoystick(),
            KeyboardMouse => CreateKeyboardMouse(),
            Racing => CreateRacing(),
            _ => CreateGamepad()
        };

        if (!string.IsNullOrWhiteSpace(customName))
            profile.Name = customName.Trim();

        return profile;
    }

    public static ControllerProfile CreateGamepad()
    {
        var p = ControllerProfile.CreateDefault();
        p.Id = Guid.NewGuid().ToString("N");
        p.Name = "Gamepad";
        p.LayoutId = Gamepad;
        p.IsOfficial = true;
        return p;
    }

    public static ControllerProfile CreateDesktop()
    {
        var p = ControllerProfile.CreateDefault();
        p.Id = Guid.NewGuid().ToString("N");
        p.Name = "Desktop";
        p.LayoutId = Desktop;
        p.IsOfficial = true;
        p.RightTrackpad = TrackpadMode.AsMouse;
        p.LeftTrackpad = TrackpadMode.AsMouse;
        // Steam-ish desktop: pad clicks as mouse buttons, paddles as extras
        p.SetAction("RightTrackpadClick", OutputAction.FromMouse(MouseButtonOutput.Left));
        p.SetAction("LeftTrackpadClick", OutputAction.FromMouse(MouseButtonOutput.Right));
        p.SetAction("R4", OutputAction.FromMouse(MouseButtonOutput.Left));
        p.SetAction("L4", OutputAction.FromMouse(MouseButtonOutput.Right));
        p.SetAction("R5", OutputAction.FromKey(0x1B)); // Esc
        p.SetAction("L5", OutputAction.FromKey(0x09)); // Tab
        p.SetAction("A", OutputAction.FromKey(0x0D)); // Enter
        p.SetAction("B", OutputAction.FromKey(0x1B)); // Esc
        return p;
    }

    public static ControllerProfile CreateMouseJoystick()
    {
        var p = ControllerProfile.CreateDefault();
        p.Id = Guid.NewGuid().ToString("N");
        p.Name = "Mouse Joystick";
        p.LayoutId = MouseJoystick;
        p.IsOfficial = true;
        // Relative pad/gyro → virtual right stick (not OS mouse).
        p.RightTrackpad = TrackpadMode.AsMouseJoystick;
        p.Gyro = GyroMode.AsMouseJoystick;
        p.GyroSensitivity = 1.2f;
        p.TrackpadSensitivityX = 1.2f;
        p.TrackpadSensitivityY = 1.2f;
        p.SetAction("RightTrackpadClick", OutputAction.FromXbox(XboxOutput.RsClick));
        p.SetAction("R4", OutputAction.FromXbox(XboxOutput.A));
        p.SetAction("L4", OutputAction.FromXbox(XboxOutput.B));
        return p;
    }

    public static ControllerProfile CreateKeyboardMouse()
    {
        var p = new ControllerProfile
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Keyboard & Mouse",
            LayoutId = KeyboardMouse,
            IsOfficial = true,
            DriverId = DriverIds.SteamController,
            RightTrackpad = TrackpadMode.AsMouse,
            LeftTrackpad = TrackpadMode.Off,
            Gyro = GyroMode.Off
        };

        // Stick / dpad → WASD (digitalized via stick not supported as keys easily —
        // map dpad and also leave LeftStick as pad for games that take both)
        p.SetAction("DpadUp", OutputAction.FromKey(0x57));    // W
        p.SetAction("DpadDown", OutputAction.FromKey(0x53));  // S
        p.SetAction("DpadLeft", OutputAction.FromKey(0x41));  // A
        p.SetAction("DpadRight", OutputAction.FromKey(0x44)); // D
        p.SetAction("LeftStick", OutputAction.FromXbox(XboxOutput.LeftStick));

        p.SetAction("A", OutputAction.FromKey(0x20)); // Space
        p.SetAction("B", OutputAction.FromKey(0x10)); // Shift
        p.SetAction("X", OutputAction.FromKey(0x45)); // E
        p.SetAction("Y", OutputAction.FromKey(0x52)); // R
        p.SetAction("Lb", OutputAction.FromKey(0x51)); // Q
        p.SetAction("Rb", OutputAction.FromKey(0x46)); // F
        p.SetAction("Lt", OutputAction.FromMouse(MouseButtonOutput.Right));
        p.SetAction("Rt", OutputAction.FromMouse(MouseButtonOutput.Left));
        p.SetAction("RightTrackpadClick", OutputAction.FromMouse(MouseButtonOutput.Left));
        p.SetAction("L4", OutputAction.FromKey(0x11)); // Ctrl
        p.SetAction("R4", OutputAction.FromKey(0x20)); // Space
        p.SetAction("L5", OutputAction.FromKey(0x09)); // Tab
        p.SetAction("R5", OutputAction.FromKey(0x1B)); // Esc
        p.SetAction("View", OutputAction.FromKey(0x4D)); // M map
        p.SetAction("Menu", OutputAction.FromKey(0x1B));
        p.SetAction("Steam", OutputAction.FromXbox(XboxOutput.Guide));
        return p;
    }

    public static ControllerProfile CreateRacing()
    {
        var p = ControllerProfile.CreateDefault();
        p.Id = Guid.NewGuid().ToString("N");
        p.Name = "Racing";
        p.LayoutId = Racing;
        p.IsOfficial = true;
        p.RightTrackpad = TrackpadMode.Off;
        p.LeftTrackpad = TrackpadMode.AsDpad; // camera / radio menus
        // Triggers already gas/brake; paddles for shift
        p.SetAction("L4", OutputAction.FromXbox(XboxOutput.Lb)); // downshift
        p.SetAction("R4", OutputAction.FromXbox(XboxOutput.Rb)); // upshift
        p.SetAction("L5", OutputAction.FromXbox(XboxOutput.X));
        p.SetAction("R5", OutputAction.FromXbox(XboxOutput.Y));
        p.SetAction("A", OutputAction.FromXbox(XboxOutput.A));
        p.SetAction("B", OutputAction.FromXbox(XboxOutput.B));
        return p;
    }
}
