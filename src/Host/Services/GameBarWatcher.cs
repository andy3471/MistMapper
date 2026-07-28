using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SteamControllerBridge.Host.Services;

/// <summary>
/// Detects when the Xbox Game Bar overlay is actually visible so the bridge
/// can force stock Gamepad output for UI navigation.
/// Process presence alone is NOT enough — GameBar.exe / GameBarFTServer
/// often stay resident after the overlay is dismissed.
/// </summary>
public sealed class GameBarWatcher : IDisposable
{
    static readonly string[] ProcessNames =
    [
        "GameBar",
        "GameBarFTServer"
    ];

    const int DwmwaCloaked = 14;

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
                foreach (var proc in Process.GetProcessesByName(name))
                {
                    try
                    {
                        if (proc.HasExited) continue;
                        if (HasVisibleOverlayWindow((uint)proc.Id))
                        {
                            now = true;
                            break;
                        }
                    }
                    catch { /* ignore */ }
                    finally
                    {
                        try { proc.Dispose(); } catch { /* ignore */ }
                    }
                }

                if (now) break;
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

    static bool HasVisibleOverlayWindow(uint pid)
    {
        bool found = false;
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var windowPid);
            if (windowPid != pid) return true;
            if (!IsWindowVisible(hwnd) || IsIconic(hwnd)) return true;
            if (IsCloaked(hwnd)) return true;
            if (!GetWindowRect(hwnd, out var rect)) return true;
            int w = rect.Right - rect.Left;
            int h = rect.Bottom - rect.Top;
            // Ignore tiny/tool windows; Game Bar chrome is a real overlay.
            if (w < 80 || h < 80) return true;
            found = true;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    static bool IsCloaked(IntPtr hwnd)
    {
        try
        {
            if (DwmGetWindowAttribute(hwnd, DwmwaCloaked, out int cloaked, sizeof(int)) != 0)
                return false;
            return cloaked != 0;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose() => _timer.Dispose();

    delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    static extern bool GetWindowRect(IntPtr hWnd, out Rect lpRect);

    [DllImport("dwmapi.dll")]
    static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

    [StructLayout(LayoutKind.Sequential)]
    struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
