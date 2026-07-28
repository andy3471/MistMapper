using System.Diagnostics;
using System.Net.Sockets;
using System.Text;

namespace SteamControllerBridge.Host.Services;

/// <summary>Probes whether VIIPER's management API is listening, and can start a local install.</summary>
public static class ViiperHealth
{
    public const string DependencyId = "viiper";
    public const string DisplayName = "VIIPER";
    public const string Host = "127.0.0.1";
    public const int Port = 3242;

    static DateTime _lastStartAttempt = DateTime.MinValue;

    public static string DefaultInstallPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VIIPER", "viiper.exe");

    public static async Task<(bool Ok, string Detail)> ProbeAsync(CancellationToken ct = default)
    {
        try
        {
            using var client = new TcpClient();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linked.CancelAfter(TimeSpan.FromMilliseconds(1200));
            await client.ConnectAsync(Host, Port, linked.Token);
            await using var stream = client.GetStream();
            // Valid null-terminated ping — bare TCP connect leaves VIIPER logging "incomplete request".
            var ping = Encoding.UTF8.GetBytes("ping\0");
            await stream.WriteAsync(ping, linked.Token);
            await stream.FlushAsync(linked.Token);
            using var ms = new MemoryStream();
            var tmp = new byte[256];
            while (true)
            {
                int n;
                try { n = await stream.ReadAsync(tmp.AsMemory(), linked.Token); }
                catch { break; }
                if (n <= 0) break;
                ms.Write(tmp, 0, n);
            }
            var body = Encoding.UTF8.GetString(ms.ToArray()).Trim();
            if (string.IsNullOrWhiteSpace(body))
                return (true, $"Connected to {Host}:{Port}");
            return (true, body.Length > 80 ? body[..80] : body);
        }
        catch (Exception ex)
        {
            return (false,
                "VIIPER unavailable. Run scripts\\install-viiper.ps1 -Start (usbip-win2 required). " +
                ex.Message);
        }
    }

    /// <summary>
    /// If the API is down and a local install exists, start <c>viiper server</c> (throttled).
    /// Does not download VIIPER — use scripts/install-viiper.ps1 for that (GPL-3.0).
    /// </summary>
    public static async Task<(bool Ok, string Detail)> EnsureRunningAsync(CancellationToken ct = default)
    {
        var probe = await ProbeAsync(ct);
        if (probe.Ok) return probe;

        if ((DateTime.UtcNow - _lastStartAttempt).TotalSeconds < 15)
            return (false, probe.Detail + " (start throttled)");

        var exe = ResolveExecutable();
        if (exe is null)
        {
            return (false,
                "VIIPER not installed. Run: powershell -ExecutionPolicy Bypass -File .\\scripts\\install-viiper.ps1 -Start");
        }

        _lastStartAttempt = DateTime.UtcNow;
        try
        {
            EnsureUsbipOnPath();
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                // Disable auto-attach: usbip VHCI discovery is flaky; host can still feed the device stream.
                // Games need a working VHCI attach separately (see docs).
                Arguments = "server --api.auto-attach-local-client=false",
                WorkingDirectory = Path.GetDirectoryName(exe)!,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Minimized,
                CreateNoWindow = true
            };
            var usbipDir = @"C:\Program Files\USBip";
            if (Directory.Exists(usbipDir))
            {
                var path = psi.Environment["PATH"] ?? Environment.GetEnvironmentVariable("PATH") ?? "";
                if (!path.Contains(usbipDir, StringComparison.OrdinalIgnoreCase))
                    psi.Environment["PATH"] = usbipDir + Path.PathSeparator + path;
            }
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            return (false, $"Failed to start VIIPER at {exe}: {ex.Message}");
        }

        for (int i = 0; i < 20 && !ct.IsCancellationRequested; i++)
        {
            await Task.Delay(250, ct);
            probe = await ProbeAsync(ct);
            if (probe.Ok)
                return (true, $"Started local VIIPER ({exe})");
        }

        return (false,
            "Started viiper.exe but API :3242 did not come up. Is usbip-win2 installed? " + probe.Detail);
    }

    public static string? ResolveExecutable()
    {
        var local = DefaultInstallPath;
        if (File.Exists(local)) return local;

        // PATH
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim('"'), "viiper.exe");
                if (File.Exists(candidate)) return candidate;
            }
            catch { /* ignore */ }
        }

        return null;
    }

    static void EnsureUsbipOnPath()
    {
        var usbipDir = @"C:\Program Files\USBip";
        if (!Directory.Exists(usbipDir)) return;
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        if (path.Contains(usbipDir, StringComparison.OrdinalIgnoreCase)) return;
        Environment.SetEnvironmentVariable("PATH", usbipDir + Path.PathSeparator + path);
    }
}
