using MistMapper.Host.Viiper;

namespace MistMapper.Tests.Fakes;

public sealed class FakeViiperClient : IViiperClient
{
    public bool IsConnected { get; private set; }
    public List<Xbox360InputState> Inputs { get; } = [];
    public event Action<byte, byte>? RumbleReceived;

    public Task ConnectAsync(CancellationToken ct = default)
    {
        IsConnected = true;
        return Task.CompletedTask;
    }

    public void SendInput(Xbox360InputState state) => Inputs.Add(state);

    public void RaiseRumble(byte left, byte right) => RumbleReceived?.Invoke(left, right);

    public void Dispose()
    {
        IsConnected = false;
    }
}
