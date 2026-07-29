using MistMapper.Shared;

namespace MistMapper.Host.Drivers;

public sealed class DriverRegistry
{
    readonly List<IControllerDriver> _drivers;

    public DriverRegistry(IEnumerable<IControllerDriver>? drivers = null)
    {
        _drivers = (drivers ?? [new SteamControllerDriver()]).ToList();
    }

    public IReadOnlyList<IControllerDriver> Drivers => _drivers;

    public IControllerDriver? FindById(string id) =>
        _drivers.FirstOrDefault(d => d.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public IControllerDriver Primary => _drivers[0];

    /// <summary>Open the first driver that connects successfully.</summary>
    public IControllerDriver? TryOpenAny()
    {
        foreach (var driver in _drivers)
        {
            if (driver.IsConnected) return driver;
            if (driver.TryOpen()) return driver;
        }
        return null;
    }

    public DriverCapabilities GetCapabilities(string? driverId = null)
    {
        var d = string.IsNullOrEmpty(driverId) ? Primary : FindById(driverId!) ?? Primary;
        return d.Capabilities;
    }
}
