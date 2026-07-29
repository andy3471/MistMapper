using MistMapper.Host.Services;

namespace MistMapper.Tests.Fakes;

public sealed class TestSteamState : ISteamState
{
    public bool IsSteamRunning { get; private set; }
    public event Action<bool>? Changed;

    public void SetRunning(bool running)
    {
        if (IsSteamRunning == running) return;
        IsSteamRunning = running;
        Changed?.Invoke(running);
    }
}
