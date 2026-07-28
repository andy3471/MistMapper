namespace SteamControllerBridge.Host;

static class Program
{
    const string MutexName = "Global\\SteamControllerBridge.Singleton";

    [STAThread]
    static void Main(string[] args)
    {
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
}
