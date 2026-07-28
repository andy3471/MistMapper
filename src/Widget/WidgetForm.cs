using SteamControllerBridge.Shared;

namespace SteamControllerBridge.Widget;

/// <summary>
/// Remapper companion UI over named-pipe IPC (same surface as the Game Bar widget).
/// Launch while the Host tray app is running. For Win+G packaging see Package.appxmanifest.
/// </summary>
public sealed class WidgetForm : Form
{
    readonly IpcClient _ipc = new();
    readonly ComboBox _profiles = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 240 };
    readonly Label _status = new() { AutoSize = true, MaximumSize = new Size(480, 0) };
    readonly CheckBox _enabled = new() { Text = "Bridge enabled", AutoSize = true };
    readonly FlowLayoutPanel _paddles = new() { AutoSize = true, WrapContents = true };
    readonly System.Windows.Forms.Timer _poll = new() { Interval = 1000 };
    ProfilesPayload? _cache;
    bool _suppressEnabledEvent;

    static readonly PhysicalInput[] PaddleInputs =
        [PhysicalInput.L4, PhysicalInput.L5, PhysicalInput.R4, PhysicalInput.R5];

    public WidgetForm()
    {
        Text = "SC Bridge — Game Bar Remapper";
        Width = 520;
        Height = 360;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10f);
        BackColor = Color.FromArgb(32, 32, 36);
        ForeColor = Color.WhiteSmoke;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            RowCount = 5,
            ColumnCount = 1
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        root.Controls.Add(_status, 0, 0);
        root.Controls.Add(_enabled, 0, 1);
        root.Controls.Add(_profiles, 0, 2);
        root.Controls.Add(new Label { Text = "Paddles (click to cycle)", AutoSize = true, ForeColor = Color.Silver }, 0, 3);
        root.Controls.Add(_paddles, 0, 4);

        foreach (Control c in new Control[] { _status, _enabled, _profiles })
            c.ForeColor = Color.WhiteSmoke;

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

        _profiles.SelectedIndexChanged += async (_, _) =>
        {
            if (_profiles.SelectedItem is ProfileItem item)
            {
                try
                {
                    await EnsureConnectedAsync();
                    await _ipc.SendAsync(IpcCommands.SetActiveProfile, new SetActiveProfilePayload { ProfileId = item.Id });
                    await RefreshAsync();
                }
                catch (Exception ex) { _status.Text = ex.Message; }
            }
        };

        _poll.Tick += async (_, _) =>
        {
            try { await RefreshAsync(); }
            catch { /* host may be restarting */ }
        };

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
                _status.Text = "Host not running. Start SteamControllerBridge.exe first.\n" + ex.Message;
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
        _status.Text = $"{status.State}: {status.Message}";
        if (_enabled.Checked != status.BridgeEnabled)
        {
            _suppressEnabledEvent = true;
            _enabled.Checked = status.BridgeEnabled;
            _suppressEnabledEvent = false;
        }

        _cache = await _ipc.GetProfilesAsync();
        var selectedId = _profiles.SelectedItem is ProfileItem pi ? pi.Id : _cache.ActiveProfileId;
        _profiles.Items.Clear();
        ProfileItem? select = null;
        foreach (var p in _cache.Profiles)
        {
            var item = new ProfileItem(p.Id, p.Name);
            _profiles.Items.Add(item);
            if (p.Id == selectedId || p.Id == _cache.ActiveProfileId) select = item;
        }
        if (select is not null) _profiles.SelectedItem = select;

        RebuildPaddles();
    }

    void RebuildPaddles()
    {
        _paddles.Controls.Clear();
        if (_cache is null || _profiles.SelectedItem is not ProfileItem item) return;
        var profile = _cache.Profiles.First(p => p.Id == item.Id);
        foreach (var paddle in PaddleInputs)
        {
            var mapped = profile.MapButton(paddle);
            var btn = new Button
            {
                Text = $"{paddle} → {mapped}",
                AutoSize = true,
                Margin = new Padding(4),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(55, 55, 62),
                ForeColor = Color.White
            };
            var captured = paddle;
            btn.Click += async (_, _) => await CyclePaddleAsync(captured);
            _paddles.Controls.Add(btn);
        }
    }

    async Task CyclePaddleAsync(PhysicalInput paddle)
    {
        if (_cache is null || _profiles.SelectedItem is not ProfileItem item) return;
        var profile = _cache.Profiles.First(p => p.Id == item.Id);
        var current = profile.MapButton(paddle);
        var options = new[]
        {
            XboxOutput.None, XboxOutput.A, XboxOutput.B, XboxOutput.X, XboxOutput.Y,
            XboxOutput.Lb, XboxOutput.Rb, XboxOutput.Back, XboxOutput.Start,
            XboxOutput.LsClick, XboxOutput.RsClick
        };
        int idx = Array.IndexOf(options, current);
        var next = options[(idx + 1) % options.Length];
        await EnsureConnectedAsync();
        await _ipc.SendAsync(IpcCommands.RemapButton, new RemapButtonPayload
        {
            ProfileId = item.Id,
            Physical = paddle.ToString(),
            Xbox = next.ToString()
        });
        await RefreshAsync();
    }

    sealed record ProfileItem(string Id, string Name)
    {
        public override string ToString() => Name;
    }
}
