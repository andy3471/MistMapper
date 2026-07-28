using SteamControllerBridge.Host.Services;
using SteamControllerBridge.Shared;

namespace SteamControllerBridge.Host.UI;

/// <summary>
/// Controller-friendly remapper UI (also used while Game Bar widget is sideloaded separately).
/// </summary>
public sealed class RemapperForm : Form
{
    readonly ProfileService _profiles;
    readonly BridgeService _bridge;

    readonly ComboBox _profileCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    readonly Label _statusLabel = new() { AutoSize = true, MaximumSize = new Size(520, 0) };
    readonly CheckBox _enabledCheck = new() { Text = "Bridge enabled", AutoSize = true };
    readonly FlowLayoutPanel _paddlePanel = new() { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = true };
    readonly DataGridView _grid = new()
    {
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        Dock = DockStyle.Fill,
        RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect
    };
    bool _suppressEnabled;
    bool _suppressUi;
    ComboBox? _leftPad;
    ComboBox? _rightPad;

    static readonly PhysicalInput[] Remappable =
    [
        PhysicalInput.A, PhysicalInput.B, PhysicalInput.X, PhysicalInput.Y,
        PhysicalInput.Lb, PhysicalInput.Rb, PhysicalInput.View, PhysicalInput.Menu, PhysicalInput.Steam,
        PhysicalInput.LsClick, PhysicalInput.RsClick,
        PhysicalInput.DpadUp, PhysicalInput.DpadDown, PhysicalInput.DpadLeft, PhysicalInput.DpadRight,
        PhysicalInput.L4, PhysicalInput.L5, PhysicalInput.R4, PhysicalInput.R5,
        PhysicalInput.Lt, PhysicalInput.Rt,
        PhysicalInput.LeftTrackpadClick, PhysicalInput.RightTrackpadClick
    ];

    static readonly PhysicalInput[] Paddles =
        [PhysicalInput.L4, PhysicalInput.L5, PhysicalInput.R4, PhysicalInput.R5];

    public RemapperForm(ProfileService profiles, BridgeService bridge)
    {
        _profiles = profiles;
        _bridge = bridge;

        Text = "Steam Controller Bridge — Remapper";
        Width = 720;
        Height = 640;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10f);

        var top = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 120,
            Padding = new Padding(12),
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false
        };

        top.Controls.Add(new Label { Text = "Profile", AutoSize = true });
        top.Controls.Add(_profileCombo);
        top.Controls.Add(_enabledCheck);
        top.Controls.Add(_statusLabel);
        top.Controls.Add(new Label { Text = "Quick paddles", AutoSize = true, Padding = new Padding(0, 8, 0, 0) });
        top.Controls.Add(_paddlePanel);

        var trackpads = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 48,
            Padding = new Padding(12, 4, 12, 8)
        };
        _leftPad = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
        _rightPad = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
        _leftPad.Items.AddRange(Enum.GetNames<TrackpadMode>());
        _rightPad.Items.AddRange(Enum.GetNames<TrackpadMode>());
        _leftPad.SelectedIndexChanged += (_, _) =>
        {
            if (_suppressUi) return;
            if (_profileCombo.SelectedItem is ProfileItem item && _leftPad.SelectedItem is string mode)
                _profiles.SetTrackpad(item.Id, left: true, Enum.Parse<TrackpadMode>(mode));
        };
        _rightPad.SelectedIndexChanged += (_, _) =>
        {
            if (_suppressUi) return;
            if (_profileCombo.SelectedItem is ProfileItem item && _rightPad.SelectedItem is string mode)
                _profiles.SetTrackpad(item.Id, left: false, Enum.Parse<TrackpadMode>(mode));
        };
        trackpads.Controls.Add(new Label { Text = "Left pad", AutoSize = true, Padding = new Padding(0, 6, 8, 0) });
        trackpads.Controls.Add(_leftPad);
        trackpads.Controls.Add(new Label { Text = "Right pad", AutoSize = true, Padding = new Padding(16, 6, 8, 0) });
        trackpads.Controls.Add(_rightPad);

        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Physical", Name = "Physical", ReadOnly = true });
        var xboxCol = new DataGridViewComboBoxColumn
        {
            HeaderText = "Xbox output",
            Name = "Xbox",
            DataSource = Enum.GetNames<XboxOutput>()
        };
        _grid.Columns.Add(xboxCol);
        _grid.CellValueChanged += GridOnCellValueChanged;
        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };

        _profileCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_suppressUi) return;
            if (_profileCombo.SelectedItem is ProfileItem selected)
                _profiles.SetActiveProfile(selected.Id);
            ReloadGrid();
        };
        _enabledCheck.CheckedChanged += (_, _) =>
        {
            if (_suppressEnabled) return;
            _bridge.SetEnabled(_enabledCheck.Checked);
        };
        _bridge.StatusChanged += OnBridgeStatus;
        _profiles.Changed += OnProfilesChanged;
        FormClosed += (_, _) =>
        {
            _bridge.StatusChanged -= OnBridgeStatus;
            _profiles.Changed -= OnProfilesChanged;
        };

        Controls.Add(_grid);
        Controls.Add(trackpads);
        Controls.Add(top);

        ReloadProfiles();
        var st = _bridge.Status;
        _statusLabel.Text = $"{st.State}: {st.Message}";
        _suppressEnabled = true;
        _enabledCheck.Checked = st.BridgeEnabled;
        _suppressEnabled = false;
    }

    void OnBridgeStatus(BridgeStatus s) => SafeBeginInvoke(() =>
    {
        _statusLabel.Text = $"{s.State}: {s.Message}";
        _suppressEnabled = true;
        _enabledCheck.Checked = s.BridgeEnabled;
        _suppressEnabled = false;
    });

    void OnProfilesChanged() => SafeBeginInvoke(ReloadProfiles);

    void SafeBeginInvoke(Action action)
    {
        if (IsDisposed) return;
        if (!IsHandleCreated)
        {
            action();
            return;
        }
        try { BeginInvoke(action); }
        catch (ObjectDisposedException) { /* closing */ }
        catch (InvalidOperationException) { action(); }
    }

    void ReloadProfiles()
    {
        if (_leftPad is null || _rightPad is null) return;
        _suppressUi = true;
        try
        {
            var doc = _profiles.Document;
            _profileCombo.Items.Clear();
            ProfileItem? selected = null;
            foreach (var p in doc.Profiles)
            {
                var item = new ProfileItem(p.Id, p.Name);
                _profileCombo.Items.Add(item);
                if (p.Id == doc.ActiveProfileId) selected = item;
            }
            if (selected is not null) _profileCombo.SelectedItem = selected;
            else if (_profileCombo.Items.Count > 0) _profileCombo.SelectedIndex = 0;
            ReloadGridCore();
            RebuildPaddles();
        }
        finally
        {
            _suppressUi = false;
        }
    }

    void ReloadGrid()
    {
        _suppressUi = true;
        try { ReloadGridCore(); RebuildPaddles(); }
        finally { _suppressUi = false; }
    }

    void ReloadGridCore()
    {
        if (_leftPad is null || _rightPad is null) return;
        if (_profileCombo.SelectedItem is not ProfileItem item) return;
        var profile = _profiles.GetProfiles().First(p => p.Id == item.Id);

        _grid.Rows.Clear();
        foreach (var phys in Remappable)
        {
            var mapped = profile.MapButton(phys).ToString();
            _grid.Rows.Add(phys.ToString(), mapped);
        }

        _leftPad.SelectedItem = profile.LeftTrackpad.ToString();
        _rightPad.SelectedItem = profile.RightTrackpad.ToString();
    }

    void RebuildPaddles()
    {
        _paddlePanel.Controls.Clear();
        if (_profileCombo.SelectedItem is not ProfileItem item) return;
        var profile = _profiles.GetProfiles().First(p => p.Id == item.Id);
        foreach (var paddle in Paddles)
        {
            var btn = new Button
            {
                Text = $"{paddle} → {profile.MapButton(paddle)}",
                AutoSize = true,
                Margin = new Padding(4)
            };
            btn.Click += (_, _) => CyclePaddle(paddle);
            _paddlePanel.Controls.Add(btn);
        }
    }

    void CyclePaddle(PhysicalInput paddle)
    {
        if (_profileCombo.SelectedItem is not ProfileItem item) return;
        var profile = _profiles.GetProfiles().First(p => p.Id == item.Id);
        var current = profile.MapButton(paddle);
        var options = new[]
        {
            XboxOutput.None, XboxOutput.A, XboxOutput.B, XboxOutput.X, XboxOutput.Y,
            XboxOutput.Lb, XboxOutput.Rb, XboxOutput.Back, XboxOutput.Start,
            XboxOutput.LsClick, XboxOutput.RsClick, XboxOutput.Lt, XboxOutput.Rt
        };
        int idx = Array.IndexOf(options, current);
        var next = options[(idx + 1) % options.Length];
        _profiles.Remap(item.Id, paddle, next);
    }

    void GridOnCellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex != 1) return;
        if (_profileCombo.SelectedItem is not ProfileItem item) return;
        var physName = _grid.Rows[e.RowIndex].Cells[0].Value?.ToString();
        var xboxName = _grid.Rows[e.RowIndex].Cells[1].Value?.ToString();
        if (physName is null || xboxName is null) return;
        if (!Enum.TryParse<PhysicalInput>(physName, out var phys)) return;
        if (!Enum.TryParse<XboxOutput>(xboxName, out var xbox)) return;
        _profiles.Remap(item.Id, phys, xbox);
        RebuildPaddles();
    }

    sealed record ProfileItem(string Id, string Name)
    {
        public override string ToString() => Name;
    }
}
