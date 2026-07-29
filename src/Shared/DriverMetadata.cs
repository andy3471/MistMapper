namespace MistMapper.Shared;

public enum DriverInputKind
{
    Digital,
    Analog,
    Stick,
    Trackpad,
    Gyro
}

public sealed class DriverInputInfo
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public DriverInputKind Kind { get; set; } = DriverInputKind.Digital;
    public bool Remappable { get; set; } = true;
}

public sealed class DriverLayoutHotspot
{
    /// <summary>Normalized 0..1 within the layout canvas.</summary>
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 0.08;
    public double Height { get; set; } = 0.08;
    public string InputId { get; set; } = "";
    public string Shape { get; set; } = "ellipse"; // ellipse | rect
    public string Label { get; set; } = "";
    public bool Remappable { get; set; } = true;
}

public sealed class DriverCapabilities
{
    public string DriverId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public List<DriverInputInfo> Inputs { get; set; } = [];
    public bool SupportsTrackpadModes { get; set; }
    public bool SupportsGyroModes { get; set; }
    public List<DriverLayoutHotspot> Layout { get; set; } = [];
}

public static class DriverIds
{
    public const string SteamController = "steam-controller";
}
