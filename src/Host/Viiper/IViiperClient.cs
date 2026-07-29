namespace MistMapper.Host.Viiper;

public interface IViiperClient : IDisposable
{
    bool IsConnected { get; }
    /// <summary>Xbox 360 rumble from the virtual pad (left motor, right motor; 0–255).</summary>
    event Action<byte, byte>? RumbleReceived;
    Task ConnectAsync(CancellationToken ct = default);
    void SendInput(Xbox360InputState state);
}
