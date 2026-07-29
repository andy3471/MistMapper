using System.Diagnostics;

namespace MistMapper.Host.Services;

public sealed class SteamWatcher : IDisposable, ISteamState
{
    readonly System.Threading.Timer _timer;
    bool _running;

    public bool IsSteamRunning => _running;
    public event Action<bool>? Changed;

    public SteamWatcher()
    {
        _timer = new System.Threading.Timer(_ => Poll(), null, 0, 1500);
    }

    void Poll()
    {
        bool now = false;
        try
        {
            now = Process.GetProcessesByName("steam").Any(p =>
            {
                try { return !p.HasExited; }
                catch { return false; }
            });
        }
        catch
        {
            now = false;
        }

        if (now == _running) return;
        _running = now;
        Changed?.Invoke(now);
    }

    public void Dispose() => _timer.Dispose();
}

public sealed class SessionWatcher : IDisposable, ISessionState
{
    bool _locked;

    public bool IsLocked => _locked;
    public event Action<bool>? Changed;

    public SessionWatcher()
    {
        Microsoft.Win32.SystemEvents.SessionSwitch += OnSessionSwitch;
    }

    void OnSessionSwitch(object sender, Microsoft.Win32.SessionSwitchEventArgs e)
    {
        bool locked = e.Reason is Microsoft.Win32.SessionSwitchReason.SessionLock
            or Microsoft.Win32.SessionSwitchReason.ConsoleDisconnect
            or Microsoft.Win32.SessionSwitchReason.RemoteDisconnect
            or Microsoft.Win32.SessionSwitchReason.SessionLogoff;

        bool unlocked = e.Reason is Microsoft.Win32.SessionSwitchReason.SessionUnlock
            or Microsoft.Win32.SessionSwitchReason.ConsoleConnect
            or Microsoft.Win32.SessionSwitchReason.RemoteConnect
            or Microsoft.Win32.SessionSwitchReason.SessionLogon;

        if (locked && !_locked)
        {
            _locked = true;
            Changed?.Invoke(true);
        }
        else if (unlocked && _locked)
        {
            _locked = false;
            Changed?.Invoke(false);
        }
    }

    public void Dispose()
    {
        Microsoft.Win32.SystemEvents.SessionSwitch -= OnSessionSwitch;
    }
}
