using MistMapper.Shared;

namespace MistMapper.Host.Drivers;

public interface IControllerDriver : IDisposable
{
    string Id { get; }
    string DisplayName { get; }
    DriverCapabilities Capabilities { get; }
    bool IsConnected { get; }

    /// <summary>Stable identity for multi-pad slots (HID physical key or test id).</summary>
    string DeviceKey { get; }

    /// <summary>"sc1", "sc2", or empty when unknown.</summary>
    string ControllerModel { get; }

    bool TryOpen();
    void Close();
    bool PrepareExclusive();
    bool RestoreExclusive();
    bool KeepAlive();
    bool TryRead(out InputFrame frame);
}
