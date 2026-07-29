using MistMapper.Host.Steam;
using MistMapper.Shared;

namespace MistMapper.Host.Drivers;

public sealed class DriverRegistry
{
    readonly List<IControllerDriver>? _injected;

    public DriverRegistry(IEnumerable<IControllerDriver>? drivers = null)
    {
        if (drivers is not null)
            _injected = drivers.ToList();
    }

    /// <summary>True when tests injected fake drivers instead of opening HID.</summary>
    public bool UsesInjectedDrivers => _injected is not null;

    public IReadOnlyList<IControllerDriver> InjectedDrivers =>
        (IReadOnlyList<IControllerDriver>?)_injected ?? Array.Empty<IControllerDriver>();

    public IReadOnlyList<IControllerDriver> Drivers =>
        UsesInjectedDrivers ? InjectedDrivers : Array.Empty<IControllerDriver>();

    public IControllerDriver? FindById(string id) =>
        Drivers.FirstOrDefault(d => d.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public IControllerDriver Primary =>
        UsesInjectedDrivers && InjectedDrivers.Count > 0
            ? InjectedDrivers[0]
            : new SteamControllerDriver();

    /// <summary>Open the first injected driver that connects (test helper).</summary>
    public IControllerDriver? TryOpenAny()
    {
        if (_injected is null) return null;
        foreach (var driver in _injected)
        {
            if (driver.IsConnected) return driver;
            if (driver.TryOpen()) return driver;
        }
        return null;
    }

    /// <summary>
    /// Enumerate physical SC1/SC2 pads not already claimed by <paramref name="excludeDeviceKeys"/>.
    /// </summary>
    public static IReadOnlyList<(string DeviceKey, string DevicePath, string Model)> EnumeratePhysicalPads(
        IEnumerable<string>? excludeDeviceKeys = null)
    {
        var list = new List<(string, string, string)>();
        foreach (var dev in SteamControllerDevice.EnumerateInstances(excludeDeviceKeys))
        {
            var path = dev.DevicePath!;
            var key = SteamControllerDevice.PhysicalDeviceKey(path);
            var model = SteamControllerDevice.ClassifyModel(dev.ProductID);
            list.Add((key, path, model));
        }
        return list;
    }

    public static SteamControllerDriver? OpenSteamController(string devicePathOrKey) =>
        SteamControllerDriver.TryOpenPath(devicePathOrKey);

    public DriverCapabilities GetCapabilities(string? driverId = null)
    {
        if (!string.IsNullOrEmpty(driverId) && FindById(driverId!) is { } found)
            return found.Capabilities;
        return SteamControllerCapabilities.Create();
    }
}
