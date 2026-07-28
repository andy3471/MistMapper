namespace SteamControllerBridge.Host;

static class Program
{
    const string MutexName = "Global\\SteamControllerBridge.Singleton";

    [STAThread]
    static void Main(string[] args)
    {
        // Ensure VIIPER can find usbip.exe when we (re)start it from this process.
        PrependToPath(@"C:\Program Files\USBip");
        PrependToPath(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VIIPER"));

        using var mutex = new Mutex(true, MutexName, out bool created);
        if (!created)
        {
            MessageBox.Show(
                "Steam Controller Bridge is already running (tray).",
                "Steam Controller Bridge",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        bool openUi = args.Any(a =>
            a.Equals("--ui", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("--remapper", StringComparison.OrdinalIgnoreCase));

        Application.Run(new TrayAppContext(openRemapperOnStart: openUi));
    }

    static void PrependToPath(string dir)
    {
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return;
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        if (path.Contains(dir, StringComparison.OrdinalIgnoreCase)) return;
        Environment.SetEnvironmentVariable("PATH", dir + Path.PathSeparator + path);
    }
}
