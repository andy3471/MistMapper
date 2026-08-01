using MistMapper.Host.Services;
using MistMapper.Shared;

namespace MistMapper.Host.UI;

/// <summary>
/// Host status panel only — remapping lives in the Game Bar widget (Win+G).
/// </summary>
public sealed class RemapperForm : Form
{
    readonly BridgeService _bridge;
    readonly Label _depBanner = new();
    readonly Label _title = new();
    readonly Label _statusLabel = new();
    readonly Label _gameLabel = new();
    readonly CheckBox _enabledCheck = new();
    readonly Label _hint = new();
    bool _suppressEnabled;

    public RemapperForm(ProfileService profiles, BridgeService bridge)
    {
        _ = profiles;
        _bridge = bridge;

        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
        Text = "MistMapper";
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = true;
        MaximizeBox = true;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10f);
        BackColor = Color.FromArgb(28, 30, 36);
        ForeColor = Color.WhiteSmoke;
        MinimumSize = new Size(420, 360);

        var work = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        ClientSize = new Size(
            Math.Min(480, Math.Max(420, work.Width / 3)),
            Math.Min(420, Math.Max(360, work.Height / 2)));

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(0),
            BackColor = BackColor
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        _depBanner.AutoSize = true;
        _depBanner.Dock = DockStyle.Top;
        _depBanner.Padding = new Padding(12, 10, 12, 10);
        _depBanner.Margin = new Padding(0);
        _depBanner.ForeColor = Color.FromArgb(255, 220, 200);
        _depBanner.BackColor = Color.FromArgb(120, 40, 40);
        _depBanner.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        _depBanner.Visible = false;
        _depBanner.MaximumSize = new Size(10000, 0);
        root.Controls.Add(_depBanner, 0, 0);

        var body = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20, 16, 20, 16),
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true
        };

        _title.Text = "MistMapper";
        _title.AutoSize = true;
        _title.Font = new Font("Segoe UI Semibold", 16f);
        _title.ForeColor = Color.FromArgb(240, 240, 244);
        _title.Margin = new Padding(0, 0, 0, 8);

        StyleBodyLabel(_statusLabel, Color.FromArgb(200, 200, 210));
        StyleBodyLabel(_gameLabel, Color.FromArgb(160, 165, 175));
        StyleBodyLabel(_hint, Color.FromArgb(180, 185, 195));

        _enabledCheck.Text = "Bridge enabled";
        _enabledCheck.AutoSize = true;
        _enabledCheck.ForeColor = Color.FromArgb(230, 230, 235);
        _enabledCheck.Margin = new Padding(0, 8, 0, 8);

        _hint.Text =
            "Remapping lives in Xbox Game Bar.\n\n" +
            "Press Win+G → open the Widgets menu → pin MistMapper.\n" +
            "Tap controls on the controller map to bind Xbox, keyboard, or mouse.";

        var openHelp = new Button
        {
            Text = "How to open Game Bar",
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(55, 95, 160),
            ForeColor = Color.White,
            Padding = new Padding(12, 6, 12, 6),
            Margin = new Padding(0, 12, 0, 0)
        };
        openHelp.FlatAppearance.BorderSize = 0;
        openHelp.Click += (_, _) =>
        {
            MessageBox.Show(
                this,
                "1. Make sure this tray host is running.\n" +
                "2. Press Win+G to open Xbox Game Bar.\n" +
                "3. Open the Widget menu and pin MistMapper.\n" +
                "4. Use the visual controller map to remap.\n\n" +
                "If the widget is missing, re-run MistMapper Setup and install the Game Bar widget.",
                "Open MistMapper in Game Bar",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        };

        body.Controls.Add(_title);
        body.Controls.Add(_statusLabel);
        body.Controls.Add(_gameLabel);
        body.Controls.Add(_enabledCheck);
        body.Controls.Add(_hint);
        body.Controls.Add(openHelp);
        root.Controls.Add(body, 0, 1);
        Controls.Add(root);

        Resize += (_, _) => UpdateWrapWidths();
        UpdateWrapWidths();

        _enabledCheck.CheckedChanged += (_, _) =>
        {
            if (_suppressEnabled) return;
            _bridge.SetEnabled(_enabledCheck.Checked);
        };
        _bridge.StatusChanged += OnBridgeStatus;
        FormClosed += (_, _) => _bridge.StatusChanged -= OnBridgeStatus;
        ApplyStatus(_bridge.Status);
    }

    static void StyleBodyLabel(Label label, Color color)
    {
        label.AutoSize = true;
        label.ForeColor = color;
        label.Margin = new Padding(0, 0, 0, 6);
        label.MaximumSize = new Size(10000, 0);
    }

    void UpdateWrapWidths()
    {
        var wrap = Math.Max(200, ClientSize.Width - 64);
        _depBanner.MaximumSize = new Size(wrap + 24, 0);
        _statusLabel.MaximumSize = new Size(wrap, 0);
        _gameLabel.MaximumSize = new Size(wrap, 0);
        _hint.MaximumSize = new Size(wrap, 0);
    }

    void OnBridgeStatus(BridgeStatus s) => SafeBeginInvoke(() => ApplyStatus(s));

    void ApplyStatus(BridgeStatus s)
    {
        _statusLabel.Text = $"{s.State}\n{s.Message}";
        _gameLabel.Text = string.IsNullOrEmpty(s.CurrentGameExe)
            ? "Foreground: (none)"
            : $"Foreground: {s.CurrentGameExe} · {s.ActiveProfileName} ({s.ActiveProfileSource})";

        var viiper = s.Dependencies.FirstOrDefault(d => d.Id == "viiper");
        if (viiper is { Ok: false })
        {
            _depBanner.Visible = true;
            _depBanner.Text = "VIIPER required — start `viiper server` (usbip-win2). " + viiper.Detail;
        }
        else
        {
            _depBanner.Visible = false;
            _depBanner.Text = "";
        }

        _suppressEnabled = true;
        _enabledCheck.Checked = s.BridgeEnabled;
        _suppressEnabled = false;
    }

    void SafeBeginInvoke(Action action)
    {
        if (IsDisposed) return;
        if (!IsHandleCreated)
        {
            action();
            return;
        }
        try { BeginInvoke(action); }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { action(); }
    }
}
