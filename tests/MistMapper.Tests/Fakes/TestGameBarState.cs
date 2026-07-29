using MistMapper.Host.Services;

namespace MistMapper.Tests.Fakes;

public sealed class TestGameBarState : IGameBarState
{
    bool _open;

    public bool IsGameBarOpen => _open;
    public event Action<bool>? Changed;

    public void SetOpen(bool open)
    {
        if (_open == open) return;
        _open = open;
        Changed?.Invoke(open);
    }

    public void Dispose() { }
}
