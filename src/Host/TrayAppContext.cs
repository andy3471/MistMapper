using MistMapper.Host.Services;
using MistMapper.Host.UI;

namespace MistMapper.Host;

public sealed class TrayAppContext : ApplicationContext
{
    readonly NotifyIcon _tray;
    readonly ProfileService _profiles;
    readonly BridgeService _bridge;
    readonly IpcServer _ipc;
    readonly GameBarFileIpcService _gameBarIpc;
    readonly SteamWatcher _steam;
    readonly SessionWatcher _session;
    RemapperForm? _remapper;

    public TrayAppContext(bool openRemapperOnStart = false)
    {
        _profiles = new ProfileService();
        _steam = new SteamWatcher();
        _session = new SessionWatcher();
        _bridge = new BridgeService(_profiles, _steam, _session);
        _ipc = new IpcServer(_profiles, _bridge);
        _gameBarIpc = new GameBarFileIpcService(_profiles, _bridge);

        StartupRegistration.SetEnabled(_profiles.Document.StartWithWindows);
        StartupRegistration.WriteFseHelperScript(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MistMapper"));

        _tray = new NotifyIcon
        {
            Visible = true,
            Text = "MistMapper",
            Icon = SystemIcons.Application,
            ContextMenuStrip = BuildMenu()
        };
        _tray.DoubleClick += (_, _) => OpenRemapper();

        _bridge.StatusChanged += OnStatus;
        _ipc.Start();
        _bridge.Start();
        OnStatus(_bridge.Status);

        if (openRemapperOnStart)
        {
            // Defer until the message loop is running so forms have handles.
            EventHandler? onIdle = null;
            onIdle = (_, _) =>
            {
                Application.Idle -= onIdle!;
                OpenRemapper();
            };
            Application.Idle += onIdle;
        }
    }

    ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Status…", null, (_, _) => OpenRemapper());
        menu.Items.Add("Toggle bridge", null, (_, _) => _bridge.SetEnabled(!_profiles.BridgeEnabled));
        var pauseSteam = new ToolStripMenuItem("Pause when Steam is running")
        {
            Checked = _profiles.AutoPauseWhenSteamRunning,
            CheckOnClick = true
        };
        pauseSteam.CheckedChanged += (_, _) =>
            _profiles.AutoPauseWhenSteamRunning = pauseSteam.Checked;
        menu.Items.Add(pauseSteam);
        menu.Opening += (_, _) =>
        {
            pauseSteam.Checked = _profiles.AutoPauseWhenSteamRunning;
        };
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Remap in Game Bar (Win+G)", null, (_, _) =>
        {
            MessageBox.Show(
                "Press Win+G, open the Widgets menu, and pin MistMapper.\n" +
                "All remapping happens in that widget.",
                "MistMapper",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        });
        menu.Items.Add(new ToolStripSeparator());
        var startup = new ToolStripMenuItem("Start with Windows")
        {
            Checked = StartupRegistration.IsEnabled(),
            CheckOnClick = true
        };
        startup.CheckedChanged += (_, _) => StartupRegistration.SetEnabled(startup.Checked);
        menu.Items.Add(startup);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Exit());
        return menu;
    }

    void OnStatus(Shared.BridgeStatus status)
    {
        try
        {
            var viiper = status.Dependencies.FirstOrDefault(d => d.Id == "viiper");
            var tip = status.State == Shared.BridgeRunState.Error && viiper is { Ok: false }
                ? $"MistMapper — VIIPER missing"
                : $"MistMapper — {status.State}";
            _tray.Text = Truncate(tip, 63);
            _tray.Icon = status.State == Shared.BridgeRunState.Error
                ? SystemIcons.Error
                : SystemIcons.Application;

            if (_bridge.ConsumeViiperDownNotification())
            {
                _tray.BalloonTipTitle = "VIIPER required";
                _tray.BalloonTipText = viiper?.Detail ?? status.Message;
                _tray.BalloonTipIcon = ToolTipIcon.Error;
                _tray.ShowBalloonTip(5000);
            }
        }
        catch { /* ignore */ }
    }

    static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..(max - 1)] + "…";

    public void OpenRemapper()
    {
        if (_remapper is { IsDisposed: false })
        {
            _remapper.Activate();
            return;
        }
        _remapper = new RemapperForm(_profiles, _bridge);
        _remapper.FormClosed += (_, _) => _remapper = null;
        _remapper.Show();
    }

    void Exit()
    {
        _tray.Visible = false;
        _bridge.Dispose();
        _ipc.Dispose();
        _gameBarIpc.Dispose();
        _steam.Dispose();
        _session.Dispose();
        _tray.Dispose();
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tray.Visible = false;
            _bridge.Dispose();
            _ipc.Dispose();
            _gameBarIpc.Dispose();
            _steam.Dispose();
            _session.Dispose();
            _tray.Dispose();
        }
        base.Dispose(disposing);
    }
}
