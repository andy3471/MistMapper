namespace MistMapper.Installer;

sealed class SetupForm : Form
{
    readonly Label _title = new();
    readonly Label _subtitle = new();
    readonly CheckBox _chkHost = new();
    readonly CheckBox _chkWidget = new();
    readonly CheckBox _chkViiper = new();
    readonly CheckBox _chkUsbip = new();
    readonly CheckBox _chkStartup = new();
    readonly CheckBox _chkLaunch = new();
    readonly ProgressBar _progress = new();
    readonly TextBox _log = new();
    readonly Button _install = new();
    readonly Button _close = new();
    CancellationTokenSource? _cts;
    bool _busy;

    public SetupForm()
    {
        Text = "MistMapper Setup";
        Width = 640;
        Height = 620;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(18, 20, 26);
        ForeColor = Color.FromArgb(240, 242, 248);
        Font = new Font("Segoe UI", 10f);

        _title.Text = "MistMapper";
        _title.Font = new Font("Segoe UI Semibold", 22f);
        _title.AutoSize = true;
        _title.Location = new Point(28, 24);
        _title.ForeColor = Color.White;

        _subtitle.Text = "Install the host, Game Bar widget, and dependencies.";
        _subtitle.AutoSize = true;
        _subtitle.Location = new Point(30, 64);
        _subtitle.ForeColor = Color.FromArgb(170, 176, 190);

        var optionsY = 100;
        void PlaceCheck(CheckBox box, string text, bool on, int y)
        {
            box.Text = text;
            box.Checked = on;
            box.AutoSize = true;
            box.Location = new Point(32, y);
            box.ForeColor = Color.FromArgb(230, 234, 242);
            box.FlatStyle = FlatStyle.Flat;
            Controls.Add(box);
        }

        PlaceCheck(_chkHost, "MistMapper host (tray remapper)", true, optionsY);
        PlaceCheck(_chkWidget, "Game Bar widget (Win+G)", true, optionsY + 28);
        PlaceCheck(_chkViiper, "VIIPER virtual Xbox pad (download)", true, optionsY + 56);
        PlaceCheck(_chkUsbip, "usbip-win2 driver (download + installer)", true, optionsY + 84);
        PlaceCheck(_chkStartup, "Start MistMapper with Windows", true, optionsY + 112);
        PlaceCheck(_chkLaunch, "Launch when finished", true, optionsY + 140);

        _progress.Location = new Point(32, 280);
        _progress.Width = 560;
        _progress.Height = 18;
        _progress.Style = ProgressBarStyle.Continuous;

        _log.Multiline = true;
        _log.ReadOnly = true;
        _log.ScrollBars = ScrollBars.Vertical;
        _log.Location = new Point(32, 310);
        _log.Width = 560;
        _log.Height = 200;
        _log.BackColor = Color.FromArgb(28, 31, 40);
        _log.ForeColor = Color.FromArgb(200, 206, 220);
        _log.BorderStyle = BorderStyle.FixedSingle;
        _log.Font = new Font("Consolas", 9f);

        _install.Text = "Install";
        _install.Location = new Point(352, 530);
        _install.Width = 120;
        _install.Height = 36;
        _install.FlatStyle = FlatStyle.Flat;
        _install.BackColor = Color.FromArgb(70, 130, 240);
        _install.ForeColor = Color.White;
        _install.FlatAppearance.BorderSize = 0;
        _install.Click += async (_, _) => await InstallAsync();

        _close.Text = "Close";
        _close.Location = new Point(480, 530);
        _close.Width = 112;
        _close.Height = 36;
        _close.FlatStyle = FlatStyle.Flat;
        _close.BackColor = Color.FromArgb(45, 50, 62);
        _close.ForeColor = Color.White;
        _close.FlatAppearance.BorderSize = 0;
        _close.Click += (_, _) =>
        {
            if (_busy)
            {
                _cts?.Cancel();
                return;
            }
            Close();
        };

        Controls.Add(_title);
        Controls.Add(_subtitle);
        Controls.Add(_progress);
        Controls.Add(_log);
        Controls.Add(_install);
        Controls.Add(_close);

        AppendLog("Ready. Admin rights are used for the Game Bar widget and drivers.");
        AppendLog("Install folder: " + InstallEngine.InstallRoot);
    }

    async Task InstallAsync()
    {
        if (_busy) return;
        if (!_chkHost.Checked && !_chkWidget.Checked && !_chkViiper.Checked && !_chkUsbip.Checked)
        {
            AppendLog("Select at least one component.");
            return;
        }

        _busy = true;
        _cts = new CancellationTokenSource();
        SetOptionsEnabled(false);
        _install.Enabled = false;
        _close.Text = "Cancel";
        _progress.Value = 0;
        _log.Clear();

        var options = new InstallOptions
        {
            InstallHost = _chkHost.Checked,
            InstallGameBarWidget = _chkWidget.Checked,
            InstallViiper = _chkViiper.Checked,
            InstallUsbip = _chkUsbip.Checked,
            StartWithWindows = _chkStartup.Checked,
            LaunchWhenDone = _chkLaunch.Checked
        };

        var engine = new InstallEngine(
            msg => BeginInvoke(() => AppendLog(msg)),
            pct => BeginInvoke(() =>
            {
                _progress.Value = Math.Clamp(pct, 0, 100);
            }));

        try
        {
            await engine.RunAsync(options, _cts.Token);
            AppendLog("");
            AppendLog("Installation completed successfully.");
            _close.Text = "Finish";
        }
        catch (OperationCanceledException)
        {
            AppendLog("Cancelled.");
            _close.Text = "Close";
        }
        catch (Exception ex)
        {
            AppendLog("ERROR: " + ex.Message);
            _close.Text = "Close";
        }
        finally
        {
            _busy = false;
            _install.Enabled = true;
            SetOptionsEnabled(true);
            _cts?.Dispose();
            _cts = null;
        }
    }

    void SetOptionsEnabled(bool enabled)
    {
        _chkHost.Enabled = enabled;
        _chkWidget.Enabled = enabled;
        _chkViiper.Enabled = enabled;
        _chkUsbip.Enabled = enabled;
        _chkStartup.Enabled = enabled;
        _chkLaunch.Enabled = enabled;
    }

    void AppendLog(string line)
    {
        _log.AppendText(line + Environment.NewLine);
    }
}
