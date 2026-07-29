using MistMapper.Shared;

namespace MistMapper.Host.Drivers;

public static class DualSenseCapabilities
{
    public static DriverCapabilities Create(string? model = null)
    {
        bool edge = string.Equals(model, "dualsense-edge", StringComparison.OrdinalIgnoreCase);
        var caps = new DriverCapabilities
        {
            DriverId = DriverIds.DualSense,
            DisplayName = edge ? "DualSense Edge" : "DualSense",
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

        Add(PhysicalInput.A, DriverInputKind.Digital, name: "Cross");
        Add(PhysicalInput.B, DriverInputKind.Digital, name: "Circle");
        Add(PhysicalInput.X, DriverInputKind.Digital, name: "Square");
        Add(PhysicalInput.Y, DriverInputKind.Digital, name: "Triangle");
        Add(PhysicalInput.Lb, DriverInputKind.Digital, name: "L1");
        Add(PhysicalInput.Rb, DriverInputKind.Digital, name: "R1");
        Add(PhysicalInput.View, DriverInputKind.Digital, name: "Create");
        Add(PhysicalInput.Menu, DriverInputKind.Digital, name: "Options");
        Add(PhysicalInput.Steam, DriverInputKind.Digital, remappable: false, name: "PS (Guide)");
        Add(PhysicalInput.LsClick, DriverInputKind.Digital, name: "L3");
        Add(PhysicalInput.RsClick, DriverInputKind.Digital, name: "R3");
        Add(PhysicalInput.DpadUp, DriverInputKind.Digital);
        Add(PhysicalInput.DpadDown, DriverInputKind.Digital);
        Add(PhysicalInput.DpadLeft, DriverInputKind.Digital);
        Add(PhysicalInput.DpadRight, DriverInputKind.Digital);
        if (edge)
        {
            Add(PhysicalInput.L4, DriverInputKind.Digital, name: "Left Paddle");
            Add(PhysicalInput.R4, DriverInputKind.Digital, name: "Right Paddle");
        }
        Add(PhysicalInput.Lt, DriverInputKind.Analog, name: "L2");
        Add(PhysicalInput.Rt, DriverInputKind.Analog, name: "R2");
        Add(PhysicalInput.LeftStick, DriverInputKind.Stick);
        Add(PhysicalInput.RightStick, DriverInputKind.Stick);
        Add(PhysicalInput.RightTrackpad, DriverInputKind.Trackpad, remappable: false, name: "Touchpad");
        Add(PhysicalInput.RightTrackpadClick, DriverInputKind.Digital, name: "Touchpad Click");
        Add(PhysicalInput.Gyro, DriverInputKind.Gyro, remappable: false);

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

        Spot("Y", 0.74, 0.30, label: "△");
        Spot("X", 0.68, 0.36, label: "□");
        Spot("B", 0.80, 0.36, label: "○");
        Spot("A", 0.74, 0.42, label: "✕");
        Spot("Lb", 0.22, 0.12, 0.12, 0.05, "rect", "L1");
        Spot("Rb", 0.66, 0.12, 0.12, 0.05, "rect", "R1");
        Spot("Lt", 0.22, 0.05, 0.12, 0.05, "rect", "L2");
        Spot("Rt", 0.66, 0.05, 0.12, 0.05, "rect", "R2");
        Spot("View", 0.38, 0.32, 0.06, 0.04, "rect", "Create");
        Spot("Menu", 0.56, 0.32, 0.06, 0.04, "rect", "Options");
        Spot("Steam", 0.47, 0.40, 0.06, 0.06, "ellipse", "PS", remappable: false);
        Spot("LeftStick", 0.28, 0.48, 0.11, 0.12, "ellipse", "LS");
        Spot("RightStick", 0.58, 0.58, 0.11, 0.12, "ellipse", "RS");
        Spot("RightTrackpad", 0.47, 0.22, 0.18, 0.10, "rect", "Touchpad");
        Spot("DpadUp", 0.28, 0.58, 0.05, 0.05, "rect", "↑");
        Spot("DpadDown", 0.28, 0.68, 0.05, 0.05, "rect", "↓");
        Spot("DpadLeft", 0.23, 0.63, 0.05, 0.05, "rect", "←");
        Spot("DpadRight", 0.33, 0.63, 0.05, 0.05, "rect", "→");
        Spot("Gyro", 0.47, 0.78, 0.10, 0.06, "rect", "Gyro");
        if (edge)
        {
            Spot("L4", 0.08, 0.40, 0.07, 0.10, "rect", "L Paddle");
            Spot("R4", 0.85, 0.40, 0.07, 0.10, "rect", "R Paddle");
        }

        return caps;
    }
}
