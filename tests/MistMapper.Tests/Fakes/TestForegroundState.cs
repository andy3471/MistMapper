using MistMapper.Host.Services;

namespace MistMapper.Tests.Fakes;

public sealed class TestForegroundState : IForegroundState
{
    string _exe = "";
    string _path = "";

    public string ExeName => _exe;
    public string Path => _path;
    public event Action? Changed;

    public void Set(string exe, string path)
    {
        _exe = exe;
        _path = path;
        Changed?.Invoke();
    }

    public void Dispose() { }
}
