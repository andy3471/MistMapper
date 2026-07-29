using System.Diagnostics;

namespace MistMapper.FseHome;

/// <summary>
/// Path 3 stub: thin launcher that starts the bridge host, then optionally a real home app (Xbox/Playnite).
/// Package as gamingHome MSIX to appear under Settings &gt; Gaming &gt; Full screen experience.
/// </summary>
static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        string? host = FindHost();
        string? handoff = GetArg(args, "--launch")
                          ?? Environment.GetEnvironmentVariable("MISTMAPPER_FSE_HANDOFF")
                          ?? Environment.GetEnvironmentVariable("SCB_FSE_HANDOFF");

        if (host is not null)
            StartDetached(host, "--tray");

        if (!string.IsNullOrWhiteSpace(handoff) && File.Exists(handoff))
            StartDetached(handoff, GetArg(args, "--launch-args") ?? "");

        // Exit immediately — host stays running. Optional splash can be added later.
        // When packaged as FSE home, Windows treats this process as the home activation.
    }

    static string? FindHost()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "MistMapper.exe"),
            Path.Combine(AppContext.BaseDirectory, "..", "Host", "MistMapper.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MistMapper", "MistMapper.exe")
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    static void StartDetached(string path, string arguments)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            Arguments = arguments,
            UseShellExecute = true
        });
    }

    static string? GetArg(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        return null;
    }
}
