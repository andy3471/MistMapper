namespace MistMapper.Host.Viiper;

public interface IViiperClient : IDisposable
{
    bool IsConnected { get; }
    Task ConnectAsync(CancellationToken ct = default);
    void SendInput(Xbox360InputState state);
}
