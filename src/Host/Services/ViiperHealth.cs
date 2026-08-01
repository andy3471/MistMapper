using System.Diagnostics;
using System.Net.Sockets;
using System.Text;

namespace MistMapper.Host.Services;

/// <summary>Probes whether VIIPER's management API is listening, and can start a local install.</summary>
public sealed class ViiperHealth : IViiperHealth
{
    public string DependencyId => DependencyIdValue;
    public string DisplayName => DisplayNameValue;

    public const string DependencyIdValue = "viiper";
    public const string DisplayNameValue = "VIIPER";
    public const string Host = "127.0.0.1";
    public const int Port = 3242;

    /// <summary>Do not spawn another viiper.exe more often than this.</summary>
    static readonly TimeSpan StartThrottle = TimeSpan.FromSeconds(20);

    /// <summary>How long to wait for :3242 after spawning viiper.exe (cold FSE boots can be slow).</summary>
    static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(25);

    static readonly TimeSpan ReadyPollInterval = TimeSpan.FromMilliseconds(500);

    static DateTime _lastStartAttempt = DateTime.MinValue;

    public static string DefaultInstallPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VIIPER", "viiper.exe");

    public async Task<(bool Ok, string Detail)> ProbeAsync(CancellationToken ct = default)
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
                "VIIPER unavailable. Install via MistMapper Setup (usbip-win2 required). " +
                ex.Message);
        }
    }

    /// <summary>
    /// If the API is down and a local install exists, start <c>viiper server</c> (throttled)
    /// and wait until :3242 answers (or timeout). Does not download VIIPER.
    /// </summary>
    public async Task<(bool Ok, string Detail)> EnsureRunningAsync(CancellationToken ct = default)
    {
        var probe = await ProbeAsync(ct);
        if (probe.Ok) return probe;

        var exe = ResolveExecutable();
        if (exe is null)
        {
            return (false,
                "VIIPER not installed. Re-run MistMapper Setup and keep the VIIPER option checked.");
        }

        var sinceStart = DateTime.UtcNow - _lastStartAttempt;
        if (sinceStart < StartThrottle)
        {
            // Already spawned recently — keep polling; do not return "throttled" immediately
            // or the bridge loop will sit in Error until the user toggles the bridge.
            var waited = await WaitUntilReadyAsync(ct);
            if (waited.Ok)
                return (true, $"VIIPER ready ({exe})");
            return (false, waited.Detail + " (waiting for recently started VIIPER)");
        }

        _lastStartAttempt = DateTime.UtcNow;
        try
        {
            EnsureUsbipOnPath();
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                // Disable auto-attach: usbip VHCI discovery is flaky; host can still feed the device stream.
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

        probe = await WaitUntilReadyAsync(ct);
        if (probe.Ok)
            return (true, $"Started local VIIPER ({exe})");

        return (false,
            "Started viiper.exe but API :3242 did not come up. Is usbip-win2 installed? " + probe.Detail);
    }

    async Task<(bool Ok, string Detail)> WaitUntilReadyAsync(CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + ReadyTimeout;
        (bool Ok, string Detail) last = (false, "VIIPER not responding yet");
        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            last = await ProbeAsync(ct);
            if (last.Ok)
                return last;
            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
                break;
            var delay = remaining < ReadyPollInterval ? remaining : ReadyPollInterval;
            try { await Task.Delay(delay, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
        }
        return last;
    }

    public static string? ResolveExecutable()
    {
        var local = DefaultInstallPath;
        if (File.Exists(local)) return local;

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
