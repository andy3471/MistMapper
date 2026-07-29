namespace MistMapper.Host.Services;

public interface ISteamState
{
    bool IsSteamRunning { get; }
    event Action<bool>? Changed;
}

public interface ISessionState
{
    bool IsLocked { get; }
    event Action<bool>? Changed;
}

public interface IForegroundState : IDisposable
{
    string ExeName { get; }
    string Path { get; }
    event Action? Changed;
}

public interface IGameBarState : IDisposable
{
    bool IsGameBarOpen { get; }
    event Action<bool>? Changed;
}
