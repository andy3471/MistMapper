using MistMapper.Host.Services;

namespace MistMapper.Tests.Fakes;

public sealed class TestForegroundState : IForegroundState
{
    string _exe = "";
    string _path = "";
    string _displayName = "";

    public string ExeName => _exe;
    public string Path => _path;
    public string DisplayName => _displayName;
    public event Action? Changed;

    public void Set(string exe, string path, string? displayName = null)
    {
        _exe = exe;
        _path = path;
        _displayName = displayName
            ?? GameDisplayName.Resolve(path, exe);
        Changed?.Invoke();
    }

    public void Dispose() { }
}
