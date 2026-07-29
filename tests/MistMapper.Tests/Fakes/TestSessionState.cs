using MistMapper.Host.Services;

namespace MistMapper.Tests.Fakes;

public sealed class TestSessionState : ISessionState
{
    public bool IsLocked { get; private set; }
    public event Action<bool>? Changed;

    public void SetLocked(bool locked)
    {
        if (IsLocked == locked) return;
        IsLocked = locked;
        Changed?.Invoke(locked);
    }
}
