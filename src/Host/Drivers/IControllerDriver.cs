using SteamControllerBridge.Shared;

namespace SteamControllerBridge.Host.Drivers;

public interface IControllerDriver : IDisposable
{
    string Id { get; }
    string DisplayName { get; }
    DriverCapabilities Capabilities { get; }
    bool IsConnected { get; }

    bool TryOpen();
    void Close();
    bool PrepareExclusive();
    bool RestoreExclusive();
    bool KeepAlive();
    bool TryRead(out InputFrame frame);
}
