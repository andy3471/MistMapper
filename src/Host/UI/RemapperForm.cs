using SteamControllerBridge.Host.Services;
using SteamControllerBridge.Shared;

namespace SteamControllerBridge.Host.UI;

/// <summary>
/// Host status panel only — remapping lives in the Game Bar widget (Win+G).
/// </summary>
public sealed class RemapperForm : Form
{
    readonly ProfileService _profiles;
    readonly BridgeService _bridge;
    readonly Label _depBanner = new()
    {
        AutoSize = false,
        Dock = DockStyle.Top,
        Height = 48,
        Padding = new Padding(12, 10, 12, 10),
        ForeColor = Color.FromArgb(255, 220, 200),
        BackColor = Color.FromArgb(120, 40, 40),
        Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
        Visible = false
    };
    readonly Label _title = new()
    {
        Text = "Steam Controller Bridge",
        AutoSize = true,
        Font = new Font("Segoe UI Semibold", 16f),
        ForeColor = Color.FromArgb(240, 240, 244)
    };
    readonly Label _statusLabel = new()
    {
        AutoSize = false,
        Width = 400,
        Height = 48,
        ForeColor = Color.FromArgb(200, 200, 210)
    };
    readonly Label _gameLabel = new()
    {
        AutoSize = false,
        Width = 400,
        Height = 36,
        ForeColor = Color.FromArgb(160, 165, 175)
    };
    readonly CheckBox _enabledCheck = new()
    {
        Text = "Bridge enabled",
        AutoSize = true,
        ForeColor = Color.FromArgb(230, 230, 235)
    };
    readonly Label _hint = new()
    {
        AutoSize = false,
        Width = 400,
        Height = 100,
        ForeColor = Color.FromArgb(180, 185, 195)
    };
    bool _suppressEnabled;

    public RemapperForm(ProfileService profiles, BridgeService bridge)
    {
        _profiles = profiles;
        _bridge = bridge;

        Text = "Steam Controller Bridge";
        Width = 460;
        Height = 380;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        Font = new Font("Segoe UI", 10f);
        BackColor = Color.FromArgb(28, 30, 36);
        ForeColor = Color.WhiteSmoke;

        var body = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20, 16, 20, 16),
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false
        };

        _hint.Text =
            "Remapping lives in Xbox Game Bar.\n\n" +
            "Press Win+G → open the Widgets menu → pin SC Bridge.\n" +
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
                "3. Open the Widget menu and pin SC Bridge.\n" +
                "4. Use the visual controller map to remap.\n\n" +
                "If the widget is missing, run:\n" +
                "publish\\GameBarWidget\\Install-GameBarWidget.cmd",
                "Open SC Bridge in Game Bar",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        };

        body.Controls.Add(_title);
        body.Controls.Add(_statusLabel);
        body.Controls.Add(_gameLabel);
        body.Controls.Add(_enabledCheck);
        body.Controls.Add(_hint);
        body.Controls.Add(openHelp);

        Controls.Add(body);
        Controls.Add(_depBanner);

        _enabledCheck.CheckedChanged += (_, _) =>
        {
            if (_suppressEnabled) return;
            _bridge.SetEnabled(_enabledCheck.Checked);
        };
        _bridge.StatusChanged += OnBridgeStatus;
        FormClosed += (_, _) => _bridge.StatusChanged -= OnBridgeStatus;
        ApplyStatus(_bridge.Status);
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
