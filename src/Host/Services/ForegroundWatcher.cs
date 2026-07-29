using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MistMapper.Host.Services;

/// <summary>Tracks the foreground window's process for per-game profile switching.</summary>
public sealed class ForegroundWatcher : IDisposable, IForegroundState
{
    readonly System.Threading.Timer _timer;
    readonly object _gate = new();
    string _exe = "";
    string _path = "";
    string _displayName = "";

    public event Action? Changed;

    public string ExeName
    {
        get { lock (_gate) return _exe; }
    }

    public string Path
    {
        get { lock (_gate) return _path; }
    }

    public string DisplayName
    {
        get { lock (_gate) return _displayName; }
    }

    public ForegroundWatcher(int intervalMs = 1000)
    {
        _timer = new System.Threading.Timer(_ => Poll(), null, 0, intervalMs);
    }

    void Poll()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == nint.Zero) return;
            _ = GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0) return;

            using var proc = Process.GetProcessById((int)pid);
            string path;
            try { path = proc.MainModule?.FileName ?? ""; }
            catch { path = ""; }
            var exe = string.IsNullOrEmpty(path)
                ? proc.ProcessName + ".exe"
                : System.IO.Path.GetFileName(path);

            // Ignore our own host / Game Bar chrome so profile doesn't flap.
            if (IsIgnored(exe)) return;

            var title = GetWindowTitle(hwnd);
            var display = GameDisplayName.Resolve(path, exe, title);

            bool changed;
            lock (_gate)
            {
                changed = !string.Equals(_exe, exe, StringComparison.OrdinalIgnoreCase)
                          || !string.Equals(_path, path, StringComparison.OrdinalIgnoreCase)
                          || !string.Equals(_displayName, display, StringComparison.Ordinal);
                if (changed)
                {
                    _exe = exe;
                    _path = path;
                    _displayName = display;
                }
            }
            if (changed) Changed?.Invoke();
        }
        catch
        {
            // access denied / exited process
        }
    }

    static string GetWindowTitle(nint hwnd)
    {
        var buffer = new char[512];
        int len = GetWindowText(hwnd, buffer, buffer.Length);
        return len > 0 ? new string(buffer, 0, len) : "";
    }

    static bool IsIgnored(string exe)
    {
        return exe.Equals("MistMapper.exe", StringComparison.OrdinalIgnoreCase)
               || exe.Equals("GameBar.exe", StringComparison.OrdinalIgnoreCase)
               || exe.Equals("GameBarFTServer.exe", StringComparison.OrdinalIgnoreCase)
               || exe.Equals("XboxGameBarWidgets.exe", StringComparison.OrdinalIgnoreCase)
               || exe.Equals("ShellExperienceHost.exe", StringComparison.OrdinalIgnoreCase)
               || exe.Equals("SearchHost.exe", StringComparison.OrdinalIgnoreCase)
               || exe.Equals("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase);
    }

    [DllImport("user32.dll")]
    static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetWindowText(nint hWnd, [Out] char[] lpString, int nMaxCount);

    public void Dispose() => _timer.Dispose();
}
