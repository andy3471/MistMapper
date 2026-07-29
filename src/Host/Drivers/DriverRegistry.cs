using MistMapper.Host.DualSense;
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
    /// Enumerate physical pads (Steam Controllers + DualSense) not already claimed.
    /// </summary>
    public static IReadOnlyList<(string DeviceKey, string DevicePath, string Model, string DriverId)> EnumeratePhysicalPads(
        IEnumerable<string>? excludeDeviceKeys = null)
    {
        var exclude = excludeDeviceKeys?.ToList() ?? [];
        var list = new List<(string, string, string, string)>();

        foreach (var dev in SteamControllerDevice.EnumerateInstances(exclude))
        {
            var path = dev.DevicePath!;
            var key = SteamControllerDevice.PhysicalDeviceKey(path);
            var model = SteamControllerDevice.ClassifyModel(dev.ProductID);
            list.Add((key, path, model, DriverIds.SteamController));
        }

        var claimed = exclude.Concat(list.Select(x => x.Item1)).ToList();
        foreach (var dev in DualSenseDevice.EnumerateInstances(claimed))
        {
            var path = dev.DevicePath!;
            var key = DualSenseDevice.PhysicalDeviceKey(path);
            var model = DualSenseDevice.ClassifyModel(dev.ProductID);
            list.Add((key, path, model, DriverIds.DualSense));
        }

        return list;
    }

    public static IControllerDriver? OpenPad(string driverId, string devicePathOrKey)
    {
        if (string.Equals(driverId, DriverIds.DualSense, StringComparison.OrdinalIgnoreCase))
            return DualSenseDriver.TryOpenPath(devicePathOrKey);
        return SteamControllerDriver.TryOpenPath(devicePathOrKey);
    }

    public static SteamControllerDriver? OpenSteamController(string devicePathOrKey) =>
        SteamControllerDriver.TryOpenPath(devicePathOrKey);

    public DriverCapabilities GetCapabilities(string? driverId = null, string? model = null)
    {
        if (!string.IsNullOrEmpty(driverId) && FindById(driverId!) is { } found)
            return found.Capabilities;

        if (string.Equals(driverId, DriverIds.DualSense, StringComparison.OrdinalIgnoreCase))
            return DualSenseCapabilities.Create(model);

        return SteamControllerCapabilities.Create(model);
    }
}
