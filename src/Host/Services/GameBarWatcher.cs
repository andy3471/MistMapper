using System.Diagnostics;

namespace SteamControllerBridge.Host.Services;

/// <summary>
/// Detects when Xbox Game Bar overlay is open so the bridge can force
/// standard Gamepad output for UI navigation.
/// </summary>
public sealed class GameBarWatcher : IDisposable
{
    /// <summary>
    /// Processes that indicate the Game Bar overlay chrome is up.
    /// XboxGameBarWidgets.exe is intentionally omitted — it often stays
    /// resident after the overlay closes.
    /// </summary>
    static readonly string[] ProcessNames =
    [
        "GameBar",
        "GameBarFTServer"
    ];

    readonly System.Threading.Timer _timer;
    bool _open;

    public bool IsGameBarOpen => _open;
    public event Action<bool>? Changed;

    public GameBarWatcher(int intervalMs = 750)
    {
        _timer = new System.Threading.Timer(_ => Poll(), null, 0, intervalMs);
    }

    void Poll()
    {
        bool now = false;
        try
        {
            foreach (var name in ProcessNames)
            {
                if (Process.GetProcessesByName(name).Any(p =>
                    {
                        try { return !p.HasExited; }
                        catch { return false; }
                    }))
                {
                    now = true;
                    break;
                }
            }
        }
        catch
        {
            now = false;
        }

        if (now == _open) return;
        _open = now;
        Changed?.Invoke(now);
    }

    public void Dispose() => _timer.Dispose();
}
