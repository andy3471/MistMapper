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
    readonly ExistingInstallInfo _existing;
    readonly bool _isUpgrade;
    CancellationTokenSource? _cts;
    bool _busy;

    public SetupForm()
    {
        _existing = InstallEngine.DetectExistingInstall();
        _isUpgrade = _existing.IsUpgrade;

        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
        Text = _isUpgrade ? "MistMapper Update" : "MistMapper Setup";
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        MaximizeBox = true;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(18, 20, 26);
        ForeColor = Color.FromArgb(240, 242, 248);
        Font = new Font("Segoe UI", 10f);
        MinimumSize = new Size(520, 480);

        // Cap initial size to the working area so 200–300% scaling still fits.
        var work = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        var targetW = Math.Min(720, Math.Max(520, work.Width - 80));
        var targetH = Math.Min(700, Math.Max(480, work.Height - 80));
        ClientSize = new Size(targetW, targetH);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(24, 20, 24, 16),
            BackColor = BackColor
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // title
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // options
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // progress
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f)); // log
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // buttons

        _title.Text = "MistMapper";
        _title.Font = new Font("Segoe UI Semibold", 22f);
        _title.AutoSize = true;
        _title.ForeColor = Color.White;
        _title.Margin = new Padding(0, 0, 0, 4);

        _subtitle.Text = _isUpgrade
            ? "Update the host, Game Bar widget, and dependencies."
            : "Install the host, Game Bar widget, and dependencies.";
        _subtitle.AutoSize = true;
        _subtitle.MaximumSize = new Size(10000, 0);
        _subtitle.ForeColor = Color.FromArgb(170, 176, 190);
        _subtitle.Margin = new Padding(0, 0, 0, 12);

        var header = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = new Padding(0)
        };
        header.Controls.Add(_title);
        header.Controls.Add(_subtitle);
        root.Controls.Add(header, 0, 0);

        var options = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 12)
        };
        StyleCheck(_chkHost, "MistMapper host (tray remapper)", true);
        StyleCheck(_chkWidget, "Game Bar widget (Win+G)", true);
        StyleCheck(_chkViiper, "VIIPER virtual Xbox pad (download / upgrade)", true);
        StyleCheck(_chkUsbip, "usbip-win2 driver (download + installer)", true);
        StyleCheck(_chkStartup, "Start MistMapper with Windows", true);
        StyleCheck(_chkLaunch, "Launch when finished", true);
        options.Controls.Add(_chkHost);
        options.Controls.Add(_chkWidget);
        options.Controls.Add(_chkViiper);
        options.Controls.Add(_chkUsbip);
        options.Controls.Add(_chkStartup);

        var fseHint = new Label
        {
            Text = "Xbox / FSE HTPC: Setup also registers Start at log in so MistMapper runs inside Xbox mode, not only after leaving to desktop.",
            AutoSize = true,
            MaximumSize = new Size(10000, 0),
            ForeColor = Color.FromArgb(170, 176, 190),
            Margin = new Padding(18, 0, 0, 10)
        };
        options.Controls.Add(fseHint);
        options.Controls.Add(_chkLaunch);
        root.Controls.Add(options, 0, 1);

        _progress.Dock = DockStyle.Top;
        _progress.Height = 18;
        _progress.Margin = new Padding(0, 0, 0, 10);
        _progress.Style = ProgressBarStyle.Continuous;
        root.Controls.Add(_progress, 0, 2);

        _log.Multiline = true;
        _log.ReadOnly = true;
        _log.ScrollBars = ScrollBars.Vertical;
        _log.Dock = DockStyle.Fill;
        _log.BackColor = Color.FromArgb(28, 31, 40);
        _log.ForeColor = Color.FromArgb(200, 206, 220);
        _log.BorderStyle = BorderStyle.FixedSingle;
        _log.Font = new Font("Consolas", 9f);
        _log.Margin = new Padding(0, 0, 0, 12);
        root.Controls.Add(_log, 0, 3);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = new Padding(0)
        };

        StylePrimaryButton(_install, _isUpgrade ? "Update" : "Install");
        _install.Click += async (_, _) => await InstallAsync();

        StyleSecondaryButton(_close, "Close");
        _close.Click += (_, _) =>
        {
            if (_busy)
            {
                _cts?.Cancel();
                return;
            }
            Close();
        };

        // RightToLeft: add Close first so Install appears to its left.
        buttons.Controls.Add(_close);
        buttons.Controls.Add(_install);
        root.Controls.Add(buttons, 0, 4);

        Controls.Add(root);
        Resize += (_, _) =>
        {
            // Keep subtitle wrapping within the client width.
            _subtitle.MaximumSize = new Size(Math.Max(200, ClientSize.Width - 64), 0);
        };
        _subtitle.MaximumSize = new Size(Math.Max(200, ClientSize.Width - 64), 0);

        AppendLog(_isUpgrade
            ? "Ready to update. Running MistMapper will be stopped while files are replaced."
            : "Ready. Admin rights are used for the Game Bar widget and drivers.");
        AppendLog(_existing.Summary);
        AppendLog("Install folder: " + InstallEngine.InstallRoot);
        AppendLog("Target VIIPER: " + InstallEngine.TargetViiperVersion);
    }

    static void StyleCheck(CheckBox box, string text, bool on)
    {
        box.Text = text;
        box.Checked = on;
        box.AutoSize = true;
        box.ForeColor = Color.FromArgb(230, 234, 242);
        box.FlatStyle = FlatStyle.Flat;
        box.Margin = new Padding(0, 0, 0, 6);
    }

    static void StylePrimaryButton(Button btn, string text)
    {
        btn.Text = text;
        btn.AutoSize = true;
        btn.MinimumSize = new Size(120, 36);
        btn.Padding = new Padding(16, 6, 16, 6);
        btn.Margin = new Padding(8, 0, 0, 0);
        btn.FlatStyle = FlatStyle.Flat;
        btn.BackColor = Color.FromArgb(70, 130, 240);
        btn.ForeColor = Color.White;
        btn.FlatAppearance.BorderSize = 0;
    }

    static void StyleSecondaryButton(Button btn, string text)
    {
        btn.Text = text;
        btn.AutoSize = true;
        btn.MinimumSize = new Size(112, 36);
        btn.Padding = new Padding(16, 6, 16, 6);
        btn.Margin = new Padding(0);
        btn.FlatStyle = FlatStyle.Flat;
        btn.BackColor = Color.FromArgb(45, 50, 62);
        btn.ForeColor = Color.White;
        btn.FlatAppearance.BorderSize = 0;
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
            AppendLog(_isUpgrade
                ? "Update completed successfully."
                : "Installation completed successfully.");
            _close.Text = "Finish";

            if (options.StartWithWindows)
            {
                var open = MessageBox.Show(
                    this,
                    "MistMapper is registered to start with Windows, including Xbox mode " +
                    "\"Start at log in\".\n\n" +
                    "Settings → Apps → Startup may look wrong (known Windows bug) even when " +
                    "the setting stuck.\n\n" +
                    "Open Startup apps settings to double-check?",
                    "Xbox / FSE startup",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information);
                if (open == DialogResult.Yes)
                    InstallEngine.OpenStartupAppsSettings();
            }
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
