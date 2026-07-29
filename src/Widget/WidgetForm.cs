using MistMapper.Shared;

namespace MistMapper.Widget;

/// <summary>
/// Lightweight companion that points users to the Game Bar remapper.
/// </summary>
public sealed class WidgetForm : Form
{
    readonly IpcClient _ipc = new();
    readonly Label _status = new() { AutoSize = true, MaximumSize = new Size(420, 0) };
    readonly CheckBox _enabled = new() { Text = "Bridge enabled", AutoSize = true };
    readonly System.Windows.Forms.Timer _poll = new() { Interval = 1500 };
    bool _suppressEnabledEvent;

    public WidgetForm()
    {
        Text = "MistMapper";
        Width = 460;
        Height = 280;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10f);
        BackColor = Color.FromArgb(28, 30, 36);
        ForeColor = Color.WhiteSmoke;

        var root = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20),
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false
        };

        var title = new Label
        {
            Text = "Remap in Game Bar",
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 14f),
            ForeColor = Color.White
        };
        var hint = new Label
        {
            Text = "Press Win+G → Widgets → pin MistMapper.\nAll button, keyboard, and mouse remapping happens there.",
            AutoSize = true,
            MaximumSize = new Size(400, 0),
            ForeColor = Color.Silver,
            Padding = new Padding(0, 8, 0, 12)
        };

        _enabled.ForeColor = Color.WhiteSmoke;
        _status.ForeColor = Color.Gainsboro;

        _enabled.CheckedChanged += async (_, _) =>
        {
            if (_suppressEnabledEvent) return;
            try
            {
                await EnsureConnectedAsync();
                await _ipc.SendAsync(IpcCommands.SetBridgeEnabled, new SetBridgeEnabledPayload { Enabled = _enabled.Checked });
            }
            catch (Exception ex) { _status.Text = ex.Message; }
        };

        _poll.Tick += async (_, _) =>
        {
            try { await RefreshAsync(); }
            catch { /* host may be restarting */ }
        };

        root.Controls.Add(title);
        root.Controls.Add(hint);
        root.Controls.Add(_enabled);
        root.Controls.Add(_status);
        Controls.Add(root);

        Shown += async (_, _) =>
        {
            try
            {
                await EnsureConnectedAsync();
                await RefreshAsync();
                _poll.Start();
            }
            catch (Exception ex)
            {
                _status.Text = "Host not running. Start MistMapper.exe first.\n" + ex.Message;
            }
        };
        FormClosed += (_, _) =>
        {
            _poll.Stop();
            _ipc.Dispose();
        };
    }

    async Task EnsureConnectedAsync()
    {
        if (_ipc.IsConnected) return;
        await _ipc.ConnectAsync(3000);
    }

    async Task RefreshAsync()
    {
        await EnsureConnectedAsync();
        var status = await _ipc.GetStatusAsync();
        var viiper = status.Dependencies?.FirstOrDefault(d => d.Id == "viiper");
        _status.Text = viiper is { Ok: false }
            ? $"VIIPER required: {viiper.Detail}"
            : $"{status.State}: {status.Message}";
        if (_enabled.Checked != status.BridgeEnabled)
        {
            _suppressEnabledEvent = true;
            _enabled.Checked = status.BridgeEnabled;
            _suppressEnabledEvent = false;
        }
    }
}
