using MistMapper.Shared;

namespace MistMapper.Host.Drivers;

public static class SteamControllerCapabilities
{
    public static DriverCapabilities Create()
    {
        var caps = new DriverCapabilities
        {
            DriverId = DriverIds.SteamController,
            DisplayName = "Steam Controller",
            SupportsTrackpadModes = true,
            SupportsGyroModes = true
        };

        void Add(PhysicalInput id, DriverInputKind kind, bool remappable = true, string? name = null) =>
            caps.Inputs.Add(new DriverInputInfo
            {
                Id = id.ToString(),
                DisplayName = name ?? id.ToString(),
                Kind = kind,
                Remappable = remappable
            });

        Add(PhysicalInput.A, DriverInputKind.Digital);
        Add(PhysicalInput.B, DriverInputKind.Digital);
        Add(PhysicalInput.X, DriverInputKind.Digital);
        Add(PhysicalInput.Y, DriverInputKind.Digital);
        Add(PhysicalInput.Lb, DriverInputKind.Digital, name: "LB");
        Add(PhysicalInput.Rb, DriverInputKind.Digital, name: "RB");
        Add(PhysicalInput.View, DriverInputKind.Digital);
        Add(PhysicalInput.Menu, DriverInputKind.Digital);
        Add(PhysicalInput.Steam, DriverInputKind.Digital, remappable: false, name: "Steam (Guide)");
        Add(PhysicalInput.LsClick, DriverInputKind.Digital, name: "LS Click");
        Add(PhysicalInput.RsClick, DriverInputKind.Digital, name: "RS Click");
        Add(PhysicalInput.DpadUp, DriverInputKind.Digital);
        Add(PhysicalInput.DpadDown, DriverInputKind.Digital);
        Add(PhysicalInput.DpadLeft, DriverInputKind.Digital);
        Add(PhysicalInput.DpadRight, DriverInputKind.Digital);
        Add(PhysicalInput.L4, DriverInputKind.Digital, name: "L4 (Upper Left Grip)");
        Add(PhysicalInput.L5, DriverInputKind.Digital, name: "L5 (Lower Left Grip)");
        Add(PhysicalInput.R4, DriverInputKind.Digital, name: "R4 (Upper Right Grip)");
        Add(PhysicalInput.R5, DriverInputKind.Digital, name: "R5 (Lower Right Grip)");
        Add(PhysicalInput.Lt, DriverInputKind.Analog, name: "LT");
        Add(PhysicalInput.Rt, DriverInputKind.Analog, name: "RT");
        Add(PhysicalInput.LeftStick, DriverInputKind.Stick);
        Add(PhysicalInput.RightStick, DriverInputKind.Stick);
        Add(PhysicalInput.LeftTrackpad, DriverInputKind.Trackpad, remappable: false);
        Add(PhysicalInput.RightTrackpad, DriverInputKind.Trackpad, remappable: false);
        Add(PhysicalInput.LeftStickTouch, DriverInputKind.Digital, remappable: false, name: "LS Touch");
        Add(PhysicalInput.RightStickTouch, DriverInputKind.Digital, remappable: false, name: "RS Touch");
        Add(PhysicalInput.LeftTrackpadClick, DriverInputKind.Digital, name: "L Pad Click");
        Add(PhysicalInput.RightTrackpadClick, DriverInputKind.Digital, name: "R Pad Click");
        Add(PhysicalInput.Gyro, DriverInputKind.Gyro, remappable: false);

        // Top-down SC silhouette hotspots (normalized).
        void Spot(string id, double x, double y, double w = 0.07, double h = 0.07, string shape = "ellipse", string? label = null, bool remappable = true) =>
            caps.Layout.Add(new DriverLayoutHotspot
            {
                InputId = id,
                X = x,
                Y = y,
                Width = w,
                Height = h,
                Shape = shape,
                Label = label ?? id,
                Remappable = remappable
            });

        Spot("Y", 0.72, 0.28, label: "Y");
        Spot("X", 0.66, 0.34, label: "X");
        Spot("B", 0.78, 0.34, label: "B");
        Spot("A", 0.72, 0.40, label: "A");
        Spot("Lb", 0.22, 0.12, 0.12, 0.05, "rect", "LB");
        Spot("Rb", 0.66, 0.12, 0.12, 0.05, "rect", "RB");
        Spot("Lt", 0.22, 0.05, 0.12, 0.05, "rect", "LT");
        Spot("Rt", 0.66, 0.05, 0.12, 0.05, "rect", "RT");
        Spot("View", 0.40, 0.30, 0.06, 0.04, "rect", "Select");
        Spot("Menu", 0.54, 0.30, 0.06, 0.04, "rect", "Start");
        Spot("Steam", 0.47, 0.38, 0.06, 0.06, "ellipse", "Steam", remappable: false);
        Spot("LeftStick", 0.28, 0.42, 0.10, 0.12, "ellipse", "LS");
        Spot("RightStick", 0.55, 0.55, 0.10, 0.12, "ellipse", "RS");
        Spot("LeftTrackpad", 0.18, 0.55, 0.16, 0.16, "ellipse", "L Pad");
        Spot("RightTrackpad", 0.66, 0.55, 0.16, 0.16, "ellipse", "R Pad");
        Spot("DpadUp", 0.38, 0.48, 0.05, 0.05, "rect", "↑");
        Spot("DpadDown", 0.38, 0.58, 0.05, 0.05, "rect", "↓");
        Spot("DpadLeft", 0.33, 0.53, 0.05, 0.05, "rect", "←");
        Spot("DpadRight", 0.43, 0.53, 0.05, 0.05, "rect", "→");
        Spot("L4", 0.08, 0.35, 0.07, 0.10, "rect", "L4 Grip");
        Spot("L5", 0.08, 0.48, 0.07, 0.10, "rect", "L5 Grip");
        Spot("R4", 0.85, 0.35, 0.07, 0.10, "rect", "R4 Grip");
        Spot("R5", 0.85, 0.48, 0.07, 0.10, "rect", "R5 Grip");
        Spot("Gyro", 0.47, 0.72, 0.10, 0.06, "rect", "Gyro");

        return caps;
    }
}
