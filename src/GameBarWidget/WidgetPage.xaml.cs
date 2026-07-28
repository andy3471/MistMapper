using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Gaming.XboxGameBar;
using Windows.Data.Json;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using Windows.UI.Xaml.Shapes;

namespace SteamControllerBridge.GameBarWidget
{
    public sealed partial class WidgetPage : Page
    {
        static readonly string[] XboxOutputs =
        {
            "None", "A", "B", "X", "Y", "Lb", "Rb", "Back", "Start",
            "Guide", "LsClick", "RsClick", "Lt", "Rt",
            "DpadUp", "DpadDown", "DpadLeft", "DpadRight", "LeftStick", "RightStick"
        };

        static readonly SolidColorBrush LockedFill = Brush(90, 70, 40, 200);
        static readonly SolidColorBrush LockedStroke = Brush(220, 170, 80, 255);

        static readonly string[] MouseOutputs = { "Left", "Right", "Middle" };
        static readonly string[] TrackpadModes = { "Off", "AsMouse", "AsLeftStick", "AsRightStick", "AsDpad" };
        static readonly string[] GyroModes = { "Off", "AsRightStick", "AsMouse" };

        readonly DispatcherTimer _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        XboxGameBarWidget _widget;
        bool _suppress;
        bool _busy;
        int _misses;
        bool _hadState;
        string _activeProfileId = "";
        string _selectedInputId = "";
        string _selectedKind = "Xbox";
        string _lastLayoutKey = "";
        readonly Dictionary<string, Shape> _hotspots = new Dictionary<string, Shape>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, TextBlock> _mapLabels = new Dictionary<string, TextBlock>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, string> _inputMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, bool> _remappable = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        readonly List<(string Id, string Name, string Description)> _officialLayouts = new List<(string, string, string)>();
        List<JsonObject> _layout = new List<JsonObject>();
        bool _layoutsComboReady;

        static readonly SolidColorBrush IdleFill = Brush(70, 78, 96, 170);
        static readonly SolidColorBrush PressFill = Brush(64, 168, 255, 230);
        static readonly SolidColorBrush SelectStroke = Brush(120, 210, 255, 255);
        static readonly SolidColorBrush IdleStroke = Brush(120, 130, 150, 255);
        static readonly SolidColorBrush OkChip = Brush(28, 70, 48, 255);
        static readonly SolidColorBrush BadChip = Brush(90, 34, 34, 255);
        static readonly SolidColorBrush NeutralChip = Brush(30, 34, 43, 255);

        public WidgetPage()
        {
            InitializeComponent();
            foreach (var o in XboxOutputs)
                ActionValueCombo.Items.Add(o);
            foreach (var m in TrackpadModes)
            {
                LeftPadCombo.Items.Add(m);
                RightPadCombo.Items.Add(m);
            }
            foreach (var m in GyroModes)
                GyroCombo.Items.Add(m);

            _timer.Tick += async (_, __) => await RefreshAsync();
            ControllerCanvas.SizeChanged += (_, __) =>
            {
                _lastLayoutKey = "";
                RebuildCanvas();
            };
            HighlightKindButtons();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (_widget != null)
                _widget.VisibleChanged -= Widget_VisibleChanged;

            _widget = e.Parameter as XboxGameBarWidget;
            if (_widget != null)
                _widget.VisibleChanged += Widget_VisibleChanged;

            await ApplyVisibilityAsync(_widget == null || _widget.Visible);
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            if (_widget != null)
            {
                _widget.VisibleChanged -= Widget_VisibleChanged;
                _widget = null;
            }
            _timer.Stop();
            _ = IpcClient.ClearHeartbeatAsync();
            base.OnNavigatedFrom(e);
        }

        async void Widget_VisibleChanged(XboxGameBarWidget sender, object args)
        {
            await ApplyVisibilityAsync(sender.Visible);
        }

        async Task ApplyVisibilityAsync(bool visible)
        {
            if (visible)
            {
                if (!_timer.IsEnabled)
                    _timer.Start();
                await RefreshAsync(force: true);
            }
            else
            {
                _timer.Stop();
                // Drop heartbeat immediately so the host drops Gamepad override.
                await IpcClient.ClearHeartbeatAsync();
            }
        }

        async void RefreshButton_Click(object sender, RoutedEventArgs e) => await RefreshAsync(force: true);

        async void BridgeToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_suppress) return;
            var enabled = BridgeToggle.IsOn ? "true" : "false";
            var resp = await IpcClient.SendAsync("setBridgeEnabled", enabled);
            if (!resp.IsOk)
                StatusText.Text = resp.Error ?? "Failed to toggle bridge";
            else
                await RefreshAsync(force: true);
        }

        async void PauseSteamToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_suppress) return;
            var enabled = PauseSteamToggle.IsOn ? "true" : "false";
            var resp = await IpcClient.SendAsync("setAutoPauseWhenSteam", enabled);
            if (!resp.IsOk)
                StatusText.Text = resp.Error ?? "Failed to toggle Steam pause";
            else
                await RefreshAsync(force: true);
        }

        async void ProfilesCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppress || !(ProfilesCombo.SelectedItem is string name)) return;
            var resp = await IpcClient.SendAsync("setActiveProfileByName", name);
            if (!resp.IsOk)
                StatusText.Text = resp.Error ?? "Profile switch failed";
            else
                await RefreshAsync(force: true);
        }

        async void BindGame_Click(object sender, RoutedEventArgs e)
        {
            var resp = await IpcClient.SendAsync("bindToCurrentGame", "");
            StatusText.Text = resp.IsOk ? "Bound profile to current game." : (resp.Error ?? "Bind failed");
            await RefreshAsync(force: true);
        }

        async void NewProfile_Click(object sender, RoutedEventArgs e)
        {
            if (_officialLayouts.Count == 0)
            {
                StatusText.Text = "No official layouts available yet — refresh once host is connected.";
                return;
            }

            var layoutBox = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            foreach (var layout in _officialLayouts)
                layoutBox.Items.Add(layout.Name + " — " + layout.Description);
            layoutBox.SelectedIndex = 0;

            var nameBox = new TextBox { Header = "Profile name (optional)", PlaceholderText = "Leave blank for default" };

            var panel = new StackPanel();
            panel.Children.Add(new TextBlock
            {
                Text = "Create a new profile from an official layout:",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            });
            panel.Children.Add(layoutBox);
            panel.Children.Add(nameBox);

            var dialog = new ContentDialog
            {
                Title = "New profile",
                Content = panel,
                PrimaryButtonText = "Create",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            if (layoutBox.SelectedIndex < 0) return;

            var layoutId = _officialLayouts[layoutBox.SelectedIndex].Id;
            var name = nameBox.Text?.Trim() ?? "";
            var payload = string.IsNullOrEmpty(name) ? layoutId : layoutId + "\t" + name;
            var resp = await IpcClient.SendAsync("createFromLayout", payload);
            StatusText.Text = resp.IsOk ? "Created profile from " + _officialLayouts[layoutBox.SelectedIndex].Name
                                        : (resp.Error ?? "Create failed");
            await RefreshAsync(force: true);
        }

        async void DuplicateProfile_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_activeProfileId)) return;
            var name = await PromptTextAsync("Duplicate profile", "Name for the copy", (ProfilesCombo.SelectedItem as string) + " copy");
            if (name == null) return;
            var payload = _activeProfileId + (string.IsNullOrWhiteSpace(name) ? "" : "\t" + name.Trim());
            var resp = await IpcClient.SendAsync("duplicateProfile", payload);
            StatusText.Text = resp.IsOk ? "Duplicated profile." : (resp.Error ?? "Duplicate failed");
            await RefreshAsync(force: true);
        }

        async void RenameProfile_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_activeProfileId)) return;
            var current = ProfilesCombo.SelectedItem as string ?? "";
            var name = await PromptTextAsync("Rename profile", "New name", current);
            if (string.IsNullOrWhiteSpace(name)) return;
            var resp = await IpcClient.SendAsync("renameProfile", _activeProfileId + "\t" + name.Trim());
            StatusText.Text = resp.IsOk ? "Renamed." : (resp.Error ?? "Rename failed");
            await RefreshAsync(force: true);
        }

        async void DeleteProfile_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_activeProfileId)) return;
            var name = ProfilesCombo.SelectedItem as string ?? "this profile";
            var dialog = new ContentDialog
            {
                Title = "Delete profile?",
                Content = "Delete \"" + name + "\"? This cannot be undone.",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            var resp = await IpcClient.SendAsync("deleteProfile", _activeProfileId);
            StatusText.Text = resp.IsOk ? "Deleted." : (resp.Error ?? "Delete failed");
            await RefreshAsync(force: true);
        }

        async void LayoutsCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppress || !_layoutsComboReady) return;
            if (LayoutsCombo.SelectedIndex <= 0) return; // 0 = placeholder
            var idx = LayoutsCombo.SelectedIndex - 1;
            if (idx < 0 || idx >= _officialLayouts.Count) return;

            var layout = _officialLayouts[idx];
            var dialog = new ContentDialog
            {
                Title = "Create from " + layout.Name + "?",
                Content = layout.Description + "\n\nCreates a new editable profile based on this official layout.",
                PrimaryButtonText = "Create",
                CloseButtonText = "Cancel"
            };
            var result = await dialog.ShowAsync();
            _suppress = true;
            LayoutsCombo.SelectedIndex = 0;
            _suppress = false;
            if (result != ContentDialogResult.Primary) return;

            var resp = await IpcClient.SendAsync("createFromLayout", layout.Id);
            StatusText.Text = resp.IsOk ? "Created " + layout.Name + " profile." : (resp.Error ?? "Create failed");
            await RefreshAsync(force: true);
        }

        static async Task<string> PromptTextAsync(string title, string header, string initial)
        {
            var box = new TextBox { Header = header, Text = initial ?? "" };
            var dialog = new ContentDialog
            {
                Title = title,
                Content = box,
                PrimaryButtonText = "OK",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;
            return box.Text;
        }

        void KindButton_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button btn) || !(btn.Tag is string kind)) return;
            _selectedKind = kind;
            HighlightKindButtons();
            UpdateEditorVisibility();
        }

        void HighlightKindButtons()
        {
            StyleKind(KindXboxBtn, "Xbox");
            StyleKind(KindKeyBtn, "Key");
            StyleKind(KindMouseBtn, "Mouse");
            StyleKind(KindNoneBtn, "None");
        }

        void StyleKind(Button btn, string kind)
        {
            var on = string.Equals(_selectedKind, kind, StringComparison.OrdinalIgnoreCase);
            btn.Background = on ? Brush(40, 110, 170, 255) : Brush(40, 46, 58, 255);
            btn.Foreground = new SolidColorBrush(Colors.White);
        }

        void UpdateEditorVisibility()
        {
            KeyValueBox.Visibility = _selectedKind == "Key" ? Visibility.Visible : Visibility.Collapsed;
            ActionValueCombo.Visibility = _selectedKind == "Key" || _selectedKind == "None"
                ? Visibility.Collapsed
                : Visibility.Visible;
            if (_selectedKind == "Xbox")
            {
                ActionValueCombo.Items.Clear();
                foreach (var o in XboxOutputs) ActionValueCombo.Items.Add(o);
            }
            else if (_selectedKind == "Mouse")
            {
                ActionValueCombo.Items.Clear();
                foreach (var o in MouseOutputs) ActionValueCombo.Items.Add(o);
            }
        }

        async void ApplyAction_Click(object sender, RoutedEventArgs e) => await ApplySelectedAsync(clear: false);
        async void ClearAction_Click(object sender, RoutedEventArgs e) => await ApplySelectedAsync(clear: true);

        async Task ApplySelectedAsync(bool clear)
        {
            if (string.IsNullOrEmpty(_selectedInputId) || string.IsNullOrEmpty(_activeProfileId))
                return;

            if (IsLockedGuideInput(_selectedInputId))
            {
                StatusText.Text = "Steam / Guide is locked to Xbox Guide";
                return;
            }

            string kind = clear ? "none" : _selectedKind.ToLowerInvariant();
            string value = "";
            string mods = "0";
            if (!clear)
            {
                if (kind == "xbox" || kind == "mouse")
                    value = ActionValueCombo.SelectedItem as string ?? "None";
                else if (kind == "key")
                {
                    var parsed = ParseKey(KeyValueBox.Text ?? "");
                    if (parsed.Vk <= 0)
                    {
                        StatusText.Text = "Enter a key like A, Space, or Ctrl+F";
                        return;
                    }
                    value = parsed.Vk.ToString();
                    mods = parsed.Mods.ToString();
                }
            }

            var payload = _activeProfileId + "\t" + _selectedInputId + "\t" + kind + "\t" + value + "\t" + mods;
            var resp = await IpcClient.SendAsync("remapAction", payload);
            StatusText.Text = resp.IsOk
                ? (_selectedInputId + (clear ? " cleared" : " remapped"))
                : (resp.Error ?? "Remap failed");
            await RefreshAsync(force: true);
            RefreshSelectedCaption();
        }

        async void ModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppress || string.IsNullOrEmpty(_activeProfileId)) return;
            if (!(sender is ComboBox combo) || !(combo.SelectedItem is string mode) || !(combo.Tag is string tag))
                return;

            BridgeResponse resp;
            if (tag == "gyro")
                resp = await IpcClient.SendAsync("setGyroMode", _activeProfileId + "\t" + mode);
            else
                resp = await IpcClient.SendAsync("setTrackpadMode", _activeProfileId + "\t" + tag + "\t" + mode);

            if (!resp.IsOk)
                StatusText.Text = resp.Error ?? "Mode change failed";
            else
                await RefreshAsync(force: true);
        }

        async Task RefreshAsync(bool force = false)
        {
            if (_busy) return;
            _busy = true;
            try
            {
                await IpcClient.TouchHeartbeatAsync();
                var json = await IpcClient.ReadStateAsync();
                if (string.IsNullOrWhiteSpace(json) || !JsonObject.TryParse(json, out var state))
                {
                    _misses++;
                    // Keep last good UI unless we miss several polls in a row (avoids Host OK flicker).
                    if (!_hadState || _misses >= 4)
                    {
                        StatusText.Text = "Host offline — start SteamControllerBridge.exe";
                        DepBanner.Visibility = Visibility.Visible;
                        DepBannerText.Text = "Host not running. Start the tray app, then refresh.";
                        SetChip(ChipHost, ChipHostText, false, "Host");
                        SetChip(ChipViiper, ChipViiperText, false, "VIIPER");
                        SetChip(ChipPad, ChipPadText, false, "Pad");
                    }
                    return;
                }

                _misses = 0;
                _hadState = true;
                Bind(state, force);
            }
            catch (Exception ex)
            {
                StatusText.Text = "Refresh failed: " + ex.Message;
            }
            finally
            {
                _busy = false;
            }
        }

        void Bind(JsonObject state, bool force)
        {
            _suppress = true;
            try
            {
                var status = state.GetNamedString("statusMessage", "Connected");
                var bridgeOn = state.GetNamedBoolean("bridgeEnabled", true);
                var pauseSteam = state.GetNamedBoolean("autoPauseWhenSteam", true);
                _activeProfileId = state.GetNamedString("activeProfileId", "");
                var activeName = state.GetNamedString("activeProfileName", "");
                var game = state.GetNamedString("currentGameExe", "");
                var source = state.GetNamedString("activeProfileSource", "");
                var depError = state.GetNamedBoolean("dependencyError", false);
                var viiperOk = state.GetNamedBoolean("viiperOk", false);
                var viiperDetail = state.GetNamedString("viiperDetail", "");
                var padOk = state.GetNamedBoolean("controllerConnected", false);

                BridgeToggle.IsOn = bridgeOn;
                PauseSteamToggle.IsOn = pauseSteam;
                StatusText.Text = status;
                GameText.Text = string.IsNullOrEmpty(game)
                    ? "No foreground game · profile source: " + source
                    : "Game: " + game + " · " + activeName + " (" + source + ")";

                SetChip(ChipHost, ChipHostText, true, "Host");
                SetChip(ChipViiper, ChipViiperText, viiperOk, "VIIPER");
                SetChip(ChipPad, ChipPadText, padOk, "Pad");

                var gameBarOverride = state.GetNamedBoolean("gameBarOverrideActive", false);
                if (depError)
                {
                    DepBanner.Visibility = Visibility.Visible;
                    DepBannerText.Text = string.IsNullOrEmpty(viiperDetail)
                        ? "VIIPER is required but not available. Start `viiper server`."
                        : viiperDetail;
                }
                else
                    DepBanner.Visibility = Visibility.Collapsed;

                OverrideBanner.Visibility = gameBarOverride ? Visibility.Visible : Visibility.Collapsed;

                var selectedProfile = ProfilesCombo.SelectedItem as string;
                ProfilesCombo.Items.Clear();
                if (state.ContainsKey("profiles"))
                {
                    foreach (var item in state.GetNamedArray("profiles"))
                        ProfilesCombo.Items.Add(item.GetObject().GetNamedString("name"));
                }
                ProfilesCombo.SelectedItem = ProfilesCombo.Items
                    .Cast<object>()
                    .Select(o => o as string)
                    .FirstOrDefault(n => string.Equals(n, activeName, StringComparison.OrdinalIgnoreCase))
                    ?? selectedProfile;

                if (state.ContainsKey("officialLayouts"))
                {
                    _officialLayouts.Clear();
                    foreach (var item in state.GetNamedArray("officialLayouts"))
                    {
                        var obj = item.GetObject();
                        _officialLayouts.Add((
                            obj.GetNamedString("id"),
                            obj.GetNamedString("name"),
                            obj.GetNamedString("description")));
                    }

                    if (!_layoutsComboReady || LayoutsCombo.Items.Count != _officialLayouts.Count + 1)
                    {
                        LayoutsCombo.Items.Clear();
                        LayoutsCombo.Items.Add("Choose an official layout to create…");
                        foreach (var layout in _officialLayouts)
                            LayoutsCombo.Items.Add(layout.Name);
                        LayoutsCombo.SelectedIndex = 0;
                        _layoutsComboReady = true;
                    }
                }

                LeftPadCombo.SelectedItem = state.GetNamedString("leftTrackpad", "Off");
                RightPadCombo.SelectedItem = state.GetNamedString("rightTrackpad", "Off");
                GyroCombo.SelectedItem = state.GetNamedString("gyro", "Off");

                _inputMap.Clear();
                if (state.ContainsKey("inputMap"))
                {
                    var map = state.GetNamedObject("inputMap");
                    foreach (var key in map.Keys)
                        _inputMap[key] = map.GetNamedString(key);
                }

                var layoutChanged = false;
                if (state.ContainsKey("layout"))
                {
                    var arr = state.GetNamedArray("layout");
                    var key = arr.Count + ":" + string.Join(",", arr.Select(i => i.GetObject().GetNamedString("inputId")));
                    if (!string.Equals(key, _lastLayoutKey, StringComparison.Ordinal) || force || _hotspots.Count == 0)
                    {
                        _layout.Clear();
                        _remappable.Clear();
                        foreach (var item in arr)
                        {
                            var obj = item.GetObject();
                            _layout.Add(obj);
                            var id = obj.GetNamedString("inputId", "");
                            var canRemap = obj.GetNamedBoolean("remappable", true) && !IsLockedGuideInput(id);
                            _remappable[id] = canRemap;
                        }
                        _lastLayoutKey = key;
                        layoutChanged = true;
                    }
                    else
                    {
                        foreach (var item in arr)
                        {
                            var obj = item.GetObject();
                            var id = obj.GetNamedString("inputId", "");
                            _remappable[id] = obj.GetNamedBoolean("remappable", true) && !IsLockedGuideInput(id);
                        }
                    }
                }

                if (layoutChanged)
                    RebuildCanvas();
                else
                    RefreshMapLabels();

                var pressed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (state.ContainsKey("pressed"))
                {
                    foreach (var item in state.GetNamedArray("pressed"))
                        pressed.Add(item.GetString());
                }
                foreach (var kv in _hotspots)
                {
                    var on = pressed.Contains(kv.Key);
                    var selected = string.Equals(kv.Key, _selectedInputId, StringComparison.OrdinalIgnoreCase);
                    var locked = !IsSpotRemappable(kv.Key);
                    kv.Value.Fill = on ? PressFill : (locked ? LockedFill : IdleFill);
                    kv.Value.Stroke = selected ? SelectStroke : (locked ? LockedStroke : IdleStroke);
                    kv.Value.StrokeThickness = selected ? 2.5 : 1.4;
                }

                RefreshSelectedCaption();
            }
            finally
            {
                _suppress = false;
            }
        }

        void RefreshMapLabels()
        {
            foreach (var kv in _mapLabels)
            {
                if (IsLockedGuideInput(kv.Key))
                {
                    kv.Value.Text = "Guide";
                    kv.Value.Foreground = LockedStroke;
                    continue;
                }
                var mapped = _inputMap.ContainsKey(kv.Key) ? _inputMap[kv.Key] : "";
                kv.Value.Text = string.IsNullOrEmpty(mapped) || mapped == "None" ? "" : mapped;
                kv.Value.Foreground = new SolidColorBrush(Color.FromArgb(255, 130, 190, 230));
            }
            foreach (var kv in _hotspots)
            {
                var label = _layout.FirstOrDefault(s => s.GetNamedString("inputId") == kv.Key)?.GetNamedString("label", kv.Key) ?? kv.Key;
                var mapped = IsLockedGuideInput(kv.Key)
                    ? "Guide (locked)"
                    : (_inputMap.ContainsKey(kv.Key) ? _inputMap[kv.Key] : "None");
                ToolTipService.SetToolTip(kv.Value, label + " → " + mapped);
            }
        }

        void RefreshSelectedCaption()
        {
            if (string.IsNullOrEmpty(_selectedInputId)) return;
            var mapped = _inputMap.ContainsKey(_selectedInputId) ? _inputMap[_selectedInputId] : "None";
            SelectedInputText.Text = _selectedInputId;
            SelectedMappedText.Text = "Currently → " + mapped;

            var locked = IsLockedGuideInput(_selectedInputId);
            LockedHintText.Visibility = locked ? Visibility.Visible : Visibility.Collapsed;
            RemapEditorPanel.Visibility = locked ? Visibility.Collapsed : Visibility.Visible;
            if (locked)
                SelectedMappedText.Text = "Locked → Guide";
        }

        static bool IsLockedGuideInput(string id) =>
            id.Equals("Steam", StringComparison.OrdinalIgnoreCase) ||
            id.Equals("Guide", StringComparison.OrdinalIgnoreCase);

        bool IsSpotRemappable(string id)
        {
            if (IsLockedGuideInput(id)) return false;
            var spot = _layout.FirstOrDefault(s =>
                string.Equals(s.GetNamedString("inputId"), id, StringComparison.OrdinalIgnoreCase));
            if (spot == null) return true;
            return spot.GetNamedBoolean("remappable", true);
        }

        void RebuildCanvas()
        {
            ControllerCanvas.Children.Clear();
            _hotspots.Clear();
            _mapLabels.Clear();
            double w = ControllerCanvas.ActualWidth;
            double h = ControllerCanvas.ActualHeight;
            if (w < 10 || h < 10)
            {
                w = 380;
                h = 280;
            }

            // Soft glow plate
            var plate = new Rectangle
            {
                Width = w * 0.86,
                Height = h * 0.72,
                RadiusX = 36,
                RadiusY = 36,
                Fill = new SolidColorBrush(Color.FromArgb(255, 32, 36, 46)),
                Stroke = new SolidColorBrush(Color.FromArgb(255, 70, 78, 96)),
                StrokeThickness = 2
            };
            Canvas.SetLeft(plate, w * 0.07);
            Canvas.SetTop(plate, h * 0.14);
            ControllerCanvas.Children.Add(plate);

            // Grips
            AddGrip(w * 0.02, h * 0.30, w * 0.14, h * 0.46);
            AddGrip(w * 0.84, h * 0.30, w * 0.14, h * 0.46);

            foreach (var spot in _layout)
            {
                var id = spot.GetNamedString("inputId", "");
                var label = spot.GetNamedString("label", id);
                var shape = spot.GetNamedString("shape", "ellipse");
                var x = spot.GetNamedNumber("x", 0);
                var y = spot.GetNamedNumber("y", 0);
                var bw = spot.GetNamedNumber("width", 0.08);
                var bh = spot.GetNamedNumber("height", 0.08);

                Shape visual;
                if (shape == "rect")
                {
                    visual = new Rectangle
                    {
                        Width = Math.Max(20, w * bw),
                        Height = Math.Max(16, h * bh),
                        RadiusX = 5,
                        RadiusY = 5
                    };
                }
                else
                {
                    visual = new Ellipse
                    {
                        Width = Math.Max(20, w * bw),
                        Height = Math.Max(20, h * bh)
                    };
                }

                visual.Fill = IsSpotRemappable(id) ? IdleFill : LockedFill;
                visual.Stroke = IdleStroke;
                visual.StrokeThickness = 1.4;
                visual.Tag = id;
                visual.PointerPressed += Hotspot_PointerPressed;
                var mapped = _inputMap.ContainsKey(id) ? _inputMap[id] : "None";
                if (IsLockedGuideInput(id))
                    mapped = "Guide (locked)";
                ToolTipService.SetToolTip(visual, label + " → " + mapped);

                Canvas.SetLeft(visual, w * x);
                Canvas.SetTop(visual, h * y);
                ControllerCanvas.Children.Add(visual);
                _hotspots[id] = visual;

                var nameTb = new TextBlock
                {
                    Text = label,
                    FontSize = 10,
                    FontWeight = Windows.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 235, 240, 248)),
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(nameTb, w * x + 3);
                Canvas.SetTop(nameTb, h * y + 2);
                ControllerCanvas.Children.Add(nameTb);

                var mapTb = new TextBlock
                {
                    Text = IsLockedGuideInput(id)
                        ? "Guide"
                        : (string.IsNullOrEmpty(mapped) || mapped == "None" ? "" : mapped),
                    FontSize = 9,
                    Foreground = IsLockedGuideInput(id)
                        ? LockedStroke
                        : new SolidColorBrush(Color.FromArgb(255, 130, 190, 230)),
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(mapTb, w * x + 3);
                Canvas.SetTop(mapTb, h * y + Math.Max(16, h * bh) - 14);
                ControllerCanvas.Children.Add(mapTb);
                _mapLabels[id] = mapTb;
            }
        }

        void AddGrip(double x, double y, double w, double h)
        {
            var grip = new Rectangle
            {
                Width = w,
                Height = h,
                RadiusX = 18,
                RadiusY = 18,
                Fill = new SolidColorBrush(Color.FromArgb(255, 28, 31, 40)),
                Stroke = new SolidColorBrush(Color.FromArgb(255, 58, 64, 78)),
                StrokeThickness = 1.5
            };
            Canvas.SetLeft(grip, x);
            Canvas.SetTop(grip, y);
            ControllerCanvas.Children.Add(grip);
        }

        void Hotspot_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (!(sender is FrameworkElement fe) || !(fe.Tag is string id)) return;
            _selectedInputId = id;
            ActionPanel.Visibility = Visibility.Visible;
            RefreshSelectedCaption();

            if (IsLockedGuideInput(id))
            {
                HighlightKindButtons();
                foreach (var kv in _hotspots)
                {
                    var selected = string.Equals(kv.Key, _selectedInputId, StringComparison.OrdinalIgnoreCase);
                    var locked = !IsSpotRemappable(kv.Key);
                    kv.Value.Stroke = selected ? SelectStroke : (locked ? LockedStroke : IdleStroke);
                    kv.Value.StrokeThickness = selected ? 2.5 : 1.4;
                }
                e.Handled = true;
                return;
            }

            // Pre-fill editor from current mapping text when possible
            var mapped = _inputMap.ContainsKey(id) ? _inputMap[id] : "None";
            if (XboxOutputs.Contains(mapped))
            {
                _selectedKind = "Xbox";
                UpdateEditorVisibility();
                ActionValueCombo.SelectedItem = mapped;
            }
            else if (mapped.StartsWith("Mouse", StringComparison.OrdinalIgnoreCase))
            {
                _selectedKind = "Mouse";
                UpdateEditorVisibility();
                ActionValueCombo.SelectedItem = mapped.Replace("Mouse", "");
            }
            else if (!string.IsNullOrEmpty(mapped) && mapped != "None")
            {
                _selectedKind = "Key";
                UpdateEditorVisibility();
                KeyValueBox.Text = mapped;
            }
            else
            {
                _selectedKind = "None";
                UpdateEditorVisibility();
            }
            HighlightKindButtons();

            foreach (var kv in _hotspots)
            {
                var selected = string.Equals(kv.Key, _selectedInputId, StringComparison.OrdinalIgnoreCase);
                var locked = !IsSpotRemappable(kv.Key);
                kv.Value.Stroke = selected ? SelectStroke : (locked ? LockedStroke : IdleStroke);
                kv.Value.StrokeThickness = selected ? 2.5 : 1.4;
            }
            e.Handled = true;
        }

        static void SetChip(Border border, TextBlock text, bool ok, string label)
        {
            border.Background = ok ? OkChip : BadChip;
            text.Text = ok ? label + " OK" : label + " —";
            text.Foreground = new SolidColorBrush(ok
                ? Color.FromArgb(255, 180, 240, 200)
                : Color.FromArgb(255, 255, 200, 200));
        }

        static SolidColorBrush Brush(byte r, byte g, byte b, byte a) =>
            new SolidColorBrush(Color.FromArgb(a, r, g, b));

        static (int Vk, int Mods) ParseKey(string text)
        {
            int mods = 0;
            int vk = 0;
            if (string.IsNullOrWhiteSpace(text)) return (0, 0);
            var parts = text.Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var raw in parts)
            {
                var part = raw.Trim();
                if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)) mods |= 1;
                else if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase)) mods |= 2;
                else if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase)) mods |= 4;
                else if (part.Equals("Win", StringComparison.OrdinalIgnoreCase)) mods |= 8;
                else if (part.Length == 1) vk = char.ToUpperInvariant(part[0]);
                else if (part.Equals("Space", StringComparison.OrdinalIgnoreCase)) vk = 0x20;
                else if (part.Equals("Enter", StringComparison.OrdinalIgnoreCase)) vk = 0x0D;
                else if (part.Equals("Esc", StringComparison.OrdinalIgnoreCase)) vk = 0x1B;
                else if (part.StartsWith("F", StringComparison.OrdinalIgnoreCase) &&
                         int.TryParse(part.Substring(1), out var fn) && fn >= 1 && fn <= 12)
                    vk = 0x70 + (fn - 1);
            }
            return (vk, mods);
        }
    }
}
