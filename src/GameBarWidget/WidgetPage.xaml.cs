using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Gaming.XboxGameBar;
using Windows.Data.Json;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

namespace MistMapper.GameBarWidget
{
    public sealed partial class WidgetPage : Page
    {
        static readonly string[] XboxOutputs =
        {
            "None", "A", "B", "X", "Y", "Lb", "Rb", "Back", "Start",
            "Guide", "LsClick", "RsClick", "Lt", "Rt",
            "DpadUp", "DpadDown", "DpadLeft", "DpadRight", "LeftStick", "RightStick"
        };

        static readonly string[] MouseOutputs = { "Left", "Right", "Middle", "ScrollUp", "ScrollDown" };
        static readonly string[] TrackpadModes =
        {
            "Off", "AsMouse", "AsMouseJoystick", "AsLeftStick", "AsRightStick", "AsDpad", "FlickStick", "ScrollWheel", "ButtonPad"
        };
        static readonly string[] GyroModes = { "Off", "AsRightStick", "AsMouse", "AsMouseJoystick" };
        static readonly string[] GyroButtonModes = { "HoldToEnable", "HoldToSuppress", "Toggle" };
        static readonly string[] GyroCombineModes = { "Any", "All" };
        static readonly string[] TrackballFrictions = { "Off", "Low", "Medium", "High", "ExtraHigh" };
        static readonly (string Id, string Label)[] GyroActivationChoices =
        {
            ("RightTrackpad", "Right Trackpad Touch"),
            ("LeftTrackpad", "Left Trackpad Touch"),
            ("RightStickTouch", "Right Stick Touch"),
            ("LeftStickTouch", "Left Stick Touch"),
            ("RsClick", "Right Stick Click"),
            ("LsClick", "Left Stick Click"),
            ("R4", "R4"), ("R5", "R5"), ("L4", "L4"), ("L5", "L5"),
            ("Rb", "RB"), ("Lb", "LB"), ("Rt", "RT"), ("Lt", "LT"),
            ("A", "A"), ("B", "B"), ("X", "X"), ("Y", "Y"),
        };

        static string FormatModeLabel(string mode) => mode switch
        {
            "Off" => "Off",
            "AsMouse" => "As Mouse",
            "AsMouseJoystick" => "Mouse Joystick",
            "AsLeftStick" => "Left Stick",
            "AsRightStick" => "Right Stick",
            "AsDpad" => "D-Pad",
            "FlickStick" => "Flick Stick",
            "ScrollWheel" => "Scroll Wheel",
            "ButtonPad" => "Button Pad",
            "HoldToEnable" => "Hold to Enable Gyro",
            "HoldToSuppress" => "Hold to Suppress Gyro",
            "Toggle" => "Toggle Gyro",
            "Any" => "Any",
            "All" => "All",
            "Low" => "Low",
            "Medium" => "Medium",
            "High" => "High",
            "ExtraHigh" => "Extra High",
            _ => mode
        };

        static readonly (string Label, int Vk, int Width)[] KeyboardRow1 =
        {
            ("Esc", 0x1B, 1), ("F1", 0x70, 1), ("F2", 0x71, 1), ("F3", 0x72, 1), ("F4", 0x73, 1),
            ("F5", 0x74, 1), ("F6", 0x75, 1), ("F7", 0x76, 1), ("F8", 0x77, 1),
            ("F9", 0x78, 1), ("F10", 0x79, 1), ("F11", 0x7A, 1), ("F12", 0x7B, 1)
        };
        static readonly (string Label, int Vk, int Width)[] KeyboardRow2 =
        {
            ("1", 0x31, 1), ("2", 0x32, 1), ("3", 0x33, 1), ("4", 0x34, 1), ("5", 0x35, 1),
            ("6", 0x36, 1), ("7", 0x37, 1), ("8", 0x38, 1), ("9", 0x39, 1), ("0", 0x30, 1),
            ("-", 0xBD, 1), ("=", 0xBB, 1), ("Bksp", 0x08, 2)
        };
        static readonly (string Label, int Vk, int Width)[] KeyboardRow3 =
        {
            ("Tab", 0x09, 2), ("Q", 0x51, 1), ("W", 0x57, 1), ("E", 0x45, 1), ("R", 0x52, 1),
            ("T", 0x54, 1), ("Y", 0x59, 1), ("U", 0x55, 1), ("I", 0x49, 1), ("O", 0x4F, 1),
            ("P", 0x50, 1), ("[", 0xDB, 1), ("]", 0xDD, 1)
        };
        static readonly (string Label, int Vk, int Width)[] KeyboardRow4 =
        {
            ("Caps", 0x14, 2), ("A", 0x41, 1), ("S", 0x53, 1), ("D", 0x44, 1), ("F", 0x46, 1),
            ("G", 0x47, 1), ("H", 0x48, 1), ("J", 0x4A, 1), ("K", 0x4B, 1), ("L", 0x4C, 1),
            (";", 0xBA, 1), ("'", 0xDE, 1), ("Enter", 0x0D, 2)
        };
        static readonly (string Label, int Vk, int Width)[] KeyboardRow5 =
        {
            ("Z", 0x5A, 1), ("X", 0x58, 1), ("C", 0x43, 1), ("V", 0x56, 1),
            ("B", 0x42, 1), ("N", 0x4E, 1), ("M", 0x4D, 1), (",", 0xBC, 1),
            (".", 0xBE, 1), ("/", 0xBF, 1)
        };
        static readonly (string Label, int Vk, int Width)[] KeyboardRow6 =
        {
            ("Space", 0x20, 6), ("\\", 0xDC, 1), ("Ins", 0x2D, 1), ("Del", 0x2E, 1),
            ("Home", 0x24, 1), ("End", 0x23, 1), ("PgUp", 0x21, 1), ("PgDn", 0x22, 1)
        };
        static readonly (string Label, int Vk, int Width)[] KeyboardRow7 =
        {
            ("\u2190", 0x25, 1), ("\u2191", 0x26, 1), ("\u2193", 0x28, 1), ("\u2192", 0x27, 1)
        };

        int _stickyMods;

        // Edit panel category definitions: inputs + associated settings
        static readonly (string Name, string[] InputIds, string[] SensKeys, string[] DeadzoneKeys, string[] InvertKeys, string[] ModeKeys)[] EditCategories =
        {
            ("Buttons", new[] { "A", "B", "X", "Y", "Lb", "Rb" }, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()),
            ("Grips", new[] { "L4", "L5", "R4", "R5" }, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()),
            ("Triggers", new[] { "Lt", "Rt" }, Array.Empty<string>(), new[] { "triggerDeadzone" }, Array.Empty<string>(), Array.Empty<string>()),
            ("DPad", new[] { "DpadUp", "DpadDown", "DpadLeft", "DpadRight" }, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()),
            ("Sticks", new[] { "LeftStick", "RightStick", "LsClick", "RsClick" },
                new[] { "stickSensitivityX", "stickSensitivityY" }, new[] { "stickDeadzone" },
                new[] { "invertStickX", "invertStickY" }, Array.Empty<string>()),
            ("Trackpads", new[] { "LeftTrackpadClick", "RightTrackpadClick" },
                new[] { "trackpadSensitivityX", "trackpadSensitivityY" }, new[] { "trackpadDeadzone" },
                new[] { "invertTrackpadX", "invertTrackpadY" }, new[] { "left", "right" }),
            ("Gyro", Array.Empty<string>(),
                new[] { "gyroSensitivity", "gyroSensitivityX", "gyroSensitivityY" }, Array.Empty<string>(),
                new[] { "invertGyroX", "invertGyroY" }, new[] { "gyro" }),
            ("Menu", new[] { "View", "Menu", "Steam" }, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()),
        };

        readonly DispatcherTimer _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        XboxGameBarWidget _widget;
        bool _preferredSizeApplied;
        bool _suppress;
        bool _busy;
        int _misses;
        bool _hadState;
        string _activeProfileId = "";
        string _remapInputId = "";
        string _remapTab = "Gamepad";
        string _activeViewTab = "View";
        string _activeEditCategory = "Buttons";
        string _controllerModel = ""; // "" when disconnected; "sc1" / "sc2" when connected
        bool _controllerConnected;
        string _selectedDeviceKey = "";
        bool _selectedHasProfileOverride;
        /// <summary>Session choice after the shared-bindings prompt for the selected pad.</summary>
        string _sharedEditDecisionDeviceKey = "";
        SharedEditDecision _sharedEditDecision = SharedEditDecision.None;

        enum SharedEditDecision
        {
            None,
            AllControllers,
            ThisControllerOnly
        }

        readonly List<ControllerPadInfo> _controllers = new List<ControllerPadInfo>();
        string _lastControllersKey = "";
        string _lastLayoutKey = "";
        string _lastGameIconToken = "";
        string _lastInputMapKey = "";
        readonly Dictionary<string, string> _inputMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, bool> _remappable = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        readonly List<(string Id, string Name, string Description)> _officialLayouts = new List<(string, string, string)>();
        List<JsonObject> _layout = new List<JsonObject>();
        bool _layoutsReady;
        string _selectedProfileName = "";
        string _activeLayoutId = "";
        string _activeLayoutName = "";
        string _browseSection = "Templates"; // Templates | Yours
        string _previewLayoutId = "";
        readonly List<string> _profileNames = new List<string>();
        bool _sensitivityThrottle;

        sealed class ControllerPadInfo
        {
            public string DeviceKey;
            public string Model;
            public string DisplayName;
            public string ProfileId;
            public int Order;
            public bool Connected;
            public bool HasProfileOverride;
        }

        // Sensitivity/deadzone/invert state from host
        readonly Dictionary<string, double> _sensValues = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["stickSensitivityX"] = 1, ["stickSensitivityY"] = 1,
            ["trackpadSensitivityX"] = 1, ["trackpadSensitivityY"] = 1,
            ["gyroSensitivity"] = 1,
            ["gyroSensitivityX"] = 1, ["gyroSensitivityY"] = 1,
            ["gyroDotsPer360"] = 6545,
            ["stickDeadzone"] = 0.08, ["trackpadDeadzone"] = 0.02, ["triggerDeadzone"] = 0.05,
        };
        readonly Dictionary<string, bool> _invertValues = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
        {
            ["invertStickX"] = false, ["invertStickY"] = false,
            ["invertTrackpadX"] = false, ["invertTrackpadY"] = false,
            ["invertGyroX"] = false, ["invertGyroY"] = false,
        };
        readonly Dictionary<string, string> _modeValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["left"] = "Off", ["right"] = "Off", ["gyro"] = "Off",
        };
        readonly List<string> _gyroButtons = new List<string>();
        string _gyroButtonMode = "HoldToEnable";
        string _gyroButtonCombine = "Any";
        readonly Dictionary<string, bool> _trackballMode = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
        {
            ["left"] = true, ["right"] = true
        };
        readonly Dictionary<string, string> _trackballFriction = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["left"] = "Medium", ["right"] = "Medium"
        };
        readonly Dictionary<string, double> _padSmoothing = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["left"] = 20, ["right"] = 20
        };
        readonly Dictionary<string, double> _padVertFriction = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["left"] = 1, ["right"] = 1
        };
        readonly Dictionary<string, double> _padRotation = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["left"] = 0, ["right"] = 0
        };

        public WidgetPage()
        {
            InitializeComponent();
            _timer.Tick += async (_, __) => await RefreshAsync();
            HighlightViewEditTabs();
            ApplyControllerOutline();
            PreviewKeyDown += WidgetPage_PreviewKeyDown;
            Loaded += (_, __) =>
            {
                var core = Window.Current?.CoreWindow;
                if (core == null) return;
                core.KeyDown -= CoreWindow_KeyDown;
                core.KeyDown += CoreWindow_KeyDown;
            };
            Unloaded += (_, __) =>
            {
                var core = Window.Current?.CoreWindow;
                if (core != null)
                    core.KeyDown -= CoreWindow_KeyDown;
            };
        }

        void CoreWindow_KeyDown(Windows.UI.Core.CoreWindow sender, Windows.UI.Core.KeyEventArgs e)
        {
            // Earlier than PreviewKeyDown — still won't beat Game Bar's LT/RT dismiss.
            // Do not handle D-pad left/right: Game Bar uses those to switch widgets.
            if (FocusManager.GetFocusedElement() is TextBox) return;
            var key = e.VirtualKey;
            if (key == Windows.System.VirtualKey.GamepadY)
            { CycleViewEditTab(1); e.Handled = true; }
            // Do not bind LB/RB — Game Bar (esp. Xbox mode) uses them to switch widgets.
            // Do not bind View — it deselects/blur the widget window.
            else if (key == Windows.System.VirtualKey.GamepadB)
            {
                // Only on main Edit — overlays use B for Back via PreviewKeyDown.
                if (!IsOverlayOpen() && _activeViewTab == "Edit")
                { CycleEditCategory(1); e.Handled = true; }
            }
            else if (key == Windows.System.VirtualKey.GamepadX)
            {
                if (!IsOverlayOpen() && _activeViewTab == "Edit")
                { CycleEditCategory(-1); e.Handled = true; }
            }
            else if (key == Windows.System.VirtualKey.GamepadLeftTrigger
                     || key == Windows.System.VirtualKey.GamepadRightTrigger)
            {
                // Swallow if we see them so Game Bar is less likely to dismiss the widget.
                e.Handled = true;
            }
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (_widget != null)
                _widget.VisibleChanged -= Widget_VisibleChanged;

            _widget = e.Parameter as XboxGameBarWidget;
            if (_widget != null)
                _widget.VisibleChanged += Widget_VisibleChanged;

            await EnsurePreferredSizeAsync();
            await ApplyVisibilityAsync(_widget == null || _widget.Visible);
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            if (_widget != null)
            {
                _widget.VisibleChanged -= Widget_VisibleChanged;
                _widget = null;
            }
            _preferredSizeApplied = false;
            _timer.Stop();
            _ = IpcClient.ClearHeartbeatAsync();
            base.OnNavigatedFrom(e);
        }

        async void Widget_VisibleChanged(XboxGameBarWidget sender, object args)
        {
            if (sender.Visible)
                await EnsurePreferredSizeAsync();
            await ApplyVisibilityAsync(sender.Visible);
        }

        /// <summary>
        /// Xbox mode often opens widgets tiny; bump size when below a usable floor.
        /// Respects a user who already enlarged the window.
        /// </summary>
        async Task EnsurePreferredSizeAsync()
        {
            if (_widget == null || _preferredSizeApplied) return;
            _preferredSizeApplied = true;
            try
            {
                _widget.MinWindowSize = new Windows.Foundation.Size(420, 480);
                _widget.MaxWindowSize = new Windows.Foundation.Size(780, 960);

                var bounds = _widget.WindowBounds;
                const double preferW = 560;
                const double preferH = 680;
                if (bounds.Width < 440 || bounds.Height < 520)
                    await _widget.TryResizeWindowAsync(new Windows.Foundation.Size(preferW, preferH));
            }
            catch
            {
                // Resize is best-effort; Game Bar may reject in some display modes.
            }
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
                await IpcClient.ClearHeartbeatAsync();
            }
        }

        // ═══════════════ VIEW / EDIT tab switching ═══════════════

        void ViewEditTab_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button btn) || !(btn.Tag is string tab)) return;
            SetViewEditTab(tab);
        }

        void SetViewEditTab(string tab)
        {
            if (IsOverlayOpen()) return;
            _activeViewTab = tab;
            ViewPanel.Visibility = tab == "View" ? Visibility.Visible : Visibility.Collapsed;
            EditPanel.Visibility = tab == "Edit" ? Visibility.Visible : Visibility.Collapsed;
            HighlightViewEditTabs();

            if (tab == "Edit")
                RebuildEditCategoryTabs();
        }

        void CycleViewEditTab(int delta)
        {
            if (IsOverlayOpen()) return;
            SetViewEditTab(_activeViewTab == "View" ? "Edit" : "View");
        }

        void CycleEditCategory(int delta)
        {
            if (IsOverlayOpen() || _activeViewTab != "Edit") return;
            var names = EditCategories.Select(c => c.Name).ToList();
            if (names.Count == 0) return;
            var idx = names.FindIndex(n => n == _activeEditCategory);
            if (idx < 0) idx = 0;
            idx = (idx + delta + names.Count) % names.Count;
            _activeEditCategory = names[idx];

            // Update in place — don't rebuild tabs (that resets the horizontal scroll).
            foreach (var child in EditCategoryTabs.Children)
            {
                if (child is Button b)
                    SetTabActive(b, (b.Tag as string) == _activeEditCategory);
            }
            RebuildEditCategoryContent();
            BringActiveCategoryTabIntoView();
        }

        void BringActiveCategoryTabIntoView()
        {
            Button active = null;
            foreach (var child in EditCategoryTabs.Children)
            {
                if (child is Button b && string.Equals(b.Tag as string, _activeEditCategory, StringComparison.Ordinal))
                {
                    active = b;
                    break;
                }
            }
            if (active == null || EditCategoryTabsScroller == null) return;

            EditCategoryTabs.UpdateLayout();
            EditCategoryTabsScroller.UpdateLayout();

            var transform = active.TransformToVisual(EditCategoryTabs);
            var bounds = transform.TransformBounds(new Windows.Foundation.Rect(0, 0, active.ActualWidth, active.ActualHeight));
            var viewport = EditCategoryTabsScroller.ViewportWidth;
            if (viewport <= 0 || double.IsNaN(viewport)) return;

            var offset = EditCategoryTabsScroller.HorizontalOffset;
            double? target = null;
            const double pad = 8;
            if (bounds.Left < offset + pad)
                target = Math.Max(0, bounds.Left - pad);
            else if (bounds.Right > offset + viewport - pad)
                target = Math.Max(0, bounds.Right - viewport + pad);

            if (target.HasValue)
                EditCategoryTabsScroller.ChangeView(target.Value, null, null, disableAnimation: false);
        }

        bool IsOverlayOpen() =>
            RemapView.Visibility == Visibility.Visible
            || BrowseLayoutView.Visibility == Visibility.Visible
            || PreviewLayoutView.Visibility == Visibility.Visible
            || SettingsView.Visibility == Visibility.Visible
            || AdvancedSettingsView.Visibility == Visibility.Visible
            || ListPickerView.Visibility == Visibility.Visible;

        TaskCompletionSource<bool> _advancedSettingsTcs;
        TaskCompletionSource<int> _listPickerTcs;

        void HighlightViewEditTabs()
        {
            SetTabActive(TabView, _activeViewTab == "View");
            SetTabActive(TabEdit, _activeViewTab == "Edit");
        }

        void ApplyControllerOutline()
        {
            if (!_controllerConnected || string.IsNullOrEmpty(_controllerModel))
            {
                NoControllerBanner.Visibility = Visibility.Visible;
                ControllerLayoutGrid.Visibility = Visibility.Collapsed;
                return;
            }

            NoControllerBanner.Visibility = Visibility.Collapsed;
            ControllerLayoutGrid.Visibility = Visibility.Visible;

            var path = _controllerModel == "sc1"
                ? "ms-appx:///Assets/controller-outline-sc1.png"
                : "ms-appx:///Assets/controller-outline-sc2.png";
            ControllerOutlineImage.Source = new Windows.UI.Xaml.Media.Imaging.BitmapImage(new Uri(path));
        }

        // ═══════════════ Main view event handlers ═══════════════

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

        void Settings_Click(object sender, RoutedEventArgs e)
        {
            RebuildSettingsControllerList();
            MainView.Visibility = Visibility.Collapsed;
            SettingsView.Visibility = Visibility.Visible;
        }

        void SettingsBack_Click(object sender, RoutedEventArgs e)
        {
            SettingsView.Visibility = Visibility.Collapsed;
            MainView.Visibility = Visibility.Visible;
        }

        void RebuildSettingsControllerList()
        {
            SettingsControllerList.Children.Clear();
            var ordered = _controllers.OrderBy(c => c.Order).ToList();
            SettingsControllersEmpty.Visibility = ordered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            for (var i = 0; i < ordered.Count; i++)
            {
                var pad = ordered[i];
                var card = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };

                var title = new TextBlock
                {
                    Text = (i + 1) + ". " + PadLabel(pad),
                    FontSize = 16,
                    FontWeight = Windows.UI.Text.FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 8)
                };
                card.Children.Add(title);

                var modelHint = new TextBlock
                {
                    Text = pad.Model == "sc1" ? "Steam Controller" : (pad.Model == "sc2" ? "Steam Controller 2" : "Controller"),
                    FontSize = 12,
                    Margin = new Thickness(0, 0, 0, 8),
                    Foreground = (Windows.UI.Xaml.Media.Brush)Application.Current.Resources["SystemControlForegroundBaseMediumBrush"]
                };
                card.Children.Add(modelHint);

                // Per-pad bindings only matter with multiple pads.
                if (ordered.Count > 1)
                {
                    var layoutLine = new TextBlock
                    {
                        Text = pad.HasProfileOverride
                            ? "Bindings: custom for this pad only"
                            : "Bindings: shared with other pads",
                        FontSize = 13,
                        Margin = new Thickness(0, 0, 0, 8),
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = (Windows.UI.Xaml.Media.Brush)Application.Current.Resources["SystemControlForegroundBaseMediumBrush"]
                    };
                    card.Children.Add(layoutLine);
                }

                var actions = new StackPanel { Orientation = Orientation.Horizontal };
                void AddAction(string label, string tag, RoutedEventHandler handler, bool enabled = true)
                {
                    var btn = new Button
                    {
                        Content = label,
                        Tag = tag,
                        Style = (Style)Application.Current.Resources["PillButtonStyle"],
                        Padding = new Thickness(14, 10, 14, 10),
                        Margin = new Thickness(0, 0, 8, 0),
                        FontSize = 13,
                        IsEnabled = enabled
                    };
                    btn.Click += handler;
                    actions.Children.Add(btn);
                }

                AddAction("Identify", pad.DeviceKey, SettingsIdentify_Click);
                AddAction("Rename", pad.DeviceKey, SettingsRename_Click);
                if (ordered.Count > 1 && pad.HasProfileOverride)
                    AddAction("Share with other pads", pad.DeviceKey, SettingsUseSharedBindings_Click);
                AddAction("Up", pad.DeviceKey, ReorderUp_Click, i > 0);
                AddAction("Down", pad.DeviceKey, ReorderDown_Click, i < ordered.Count - 1);
                card.Children.Add(actions);

                SettingsControllerList.Children.Add(card);
            }
        }

        static string PadLabel(ControllerPadInfo pad)
        {
            if (!string.IsNullOrWhiteSpace(pad.DisplayName))
                return pad.DisplayName.Trim();
            return pad.Model == "sc1" ? "Steam Controller" : (pad.Model == "sc2" ? "Steam Controller 2" : "Controller");
        }

        async void SettingsIdentify_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button btn) || !(btn.Tag is string key)) return;
            StatusText.Text = "Identifying…";
            var resp = await IpcClient.SendAsync("identifyController", key);
            StatusText.Text = resp.IsOk ? "Vibrated pad" : (resp.Error ?? "Identify failed");
        }

        async void SettingsRename_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button btn) || !(btn.Tag is string key)) return;
            var pad = _controllers.FirstOrDefault(c =>
                string.Equals(c.DeviceKey, key, StringComparison.OrdinalIgnoreCase));
            var current = pad?.DisplayName ?? "";
            var name = await PromptTextAsync("Rename controller", "Name for this pad", current);
            if (name is null) return;
            var resp = await IpcClient.SendAsync("renameController", key + "\t" + name.Trim());
            StatusText.Text = resp.IsOk
                ? (string.IsNullOrWhiteSpace(name) ? "Name cleared" : "Renamed to " + name.Trim())
                : (resp.Error ?? "Rename failed");
            await RefreshAsync(force: true);
            if (SettingsView.Visibility == Visibility.Visible)
                RebuildSettingsControllerList();
        }

        async void SettingsUseSharedBindings_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button btn) || !(btn.Tag is string key)) return;
            var resp = await IpcClient.SendAsync("setControllerSlotProfile", key + "\t");
            StatusText.Text = resp.IsOk
                ? "This pad uses shared bindings again"
                : (resp.Error ?? "Could not switch to shared");
            if (string.Equals(key, _sharedEditDecisionDeviceKey, StringComparison.OrdinalIgnoreCase))
            {
                _sharedEditDecision = SharedEditDecision.None;
                _sharedEditDecisionDeviceKey = "";
            }
            await RefreshAsync(force: true);
            if (SettingsView.Visibility == Visibility.Visible)
                RebuildSettingsControllerList();
        }

        /// <summary>
        /// Before changing bindings used by multiple pads, ask once: update all, or copy for this pad.
        /// Returns false if the user cancelled.
        /// </summary>
        async Task<bool> EnsureSharedBindingsEditAllowedAsync()
        {
            if (_controllers.Count <= 1) return true;

            var pad = _controllers.FirstOrDefault(c =>
                string.Equals(c.DeviceKey, _selectedDeviceKey, StringComparison.OrdinalIgnoreCase));
            if (pad == null) return true;
            if (pad.HasProfileOverride) return true;

            if (string.Equals(_sharedEditDecisionDeviceKey, pad.DeviceKey, StringComparison.OrdinalIgnoreCase)
                && _sharedEditDecision != SharedEditDecision.None)
            {
                if (_sharedEditDecision == SharedEditDecision.ThisControllerOnly && !pad.HasProfileOverride)
                {
                    if (!await CloneBindingsForSelectedPadAsync())
                        return false;
                }
                return true;
            }

            var padName = PadLabel(pad);
            var dialog = new ContentDialog
            {
                Title = "Shared bindings",
                Content = "These bindings are used by every connected controller.\n\n"
                    + "Apply this change to all of them, or copy the layout so only \""
                    + padName + "\" is affected?",
                PrimaryButtonText = "All controllers",
                SecondaryButtonText = "This controller only",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.None)
                return false;

            _sharedEditDecisionDeviceKey = pad.DeviceKey;
            if (result == ContentDialogResult.Primary)
            {
                _sharedEditDecision = SharedEditDecision.AllControllers;
                return true;
            }

            _sharedEditDecision = SharedEditDecision.ThisControllerOnly;
            return await CloneBindingsForSelectedPadAsync();
        }

        async Task<bool> CloneBindingsForSelectedPadAsync()
        {
            if (string.IsNullOrEmpty(_selectedDeviceKey)) return false;
            var payload = _selectedDeviceKey;
            if (!string.IsNullOrEmpty(_activeProfileId))
                payload += "\t" + _activeProfileId;
            var resp = await IpcClient.SendAsync("makeControllerProfileUnique", payload);
            if (!resp.IsOk)
            {
                StatusText.Text = resp.Error ?? "Could not copy bindings for this pad";
                _sharedEditDecision = SharedEditDecision.None;
                _sharedEditDecisionDeviceKey = "";
                return false;
            }

            await RefreshAsync(force: true);
            return true;
        }

        async void ReorderUp_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button btn) || !(btn.Tag is string key)) return;
            await MoveControllerAsync(key, -1);
        }

        async void ReorderDown_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button btn) || !(btn.Tag is string key)) return;
            await MoveControllerAsync(key, 1);
        }

        async Task MoveControllerAsync(string deviceKey, int delta)
        {
            var ordered = _controllers.OrderBy(c => c.Order).ToList();
            var idx = ordered.FindIndex(c => string.Equals(c.DeviceKey, deviceKey, StringComparison.OrdinalIgnoreCase));
            if (idx < 0) return;
            var dest = idx + delta;
            if (dest < 0 || dest >= ordered.Count) return;
            var tmp = ordered[idx];
            ordered[idx] = ordered[dest];
            ordered[dest] = tmp;

            var json = new JsonArray();
            for (var i = 0; i < ordered.Count; i++)
            {
                var o = new JsonObject();
                o.SetNamedValue("deviceKey", JsonValue.CreateStringValue(ordered[i].DeviceKey));
                o.SetNamedValue("order", JsonValue.CreateNumberValue(i));
                o.SetNamedValue("displayName", JsonValue.CreateStringValue(ordered[i].DisplayName ?? ""));
                o.SetNamedValue("lastModel", JsonValue.CreateStringValue(ordered[i].Model ?? ""));
                o.SetNamedValue("enabled", JsonValue.CreateBooleanValue(true));
                json.Add(o);
            }

            var resp = await IpcClient.SendAsync("setControllerSlotOrder", json.ToString());
            if (!resp.IsOk)
                StatusText.Text = resp.Error ?? "Reorder failed";
            await RefreshAsync(force: true);
            if (SettingsView.Visibility == Visibility.Visible)
                RebuildSettingsControllerList();
        }

        async void ControllerTab_Click(object sender, RoutedEventArgs e)
        {
            if (_suppress) return;
            if (!(sender is Button btn) || !(btn.Tag is string key)) return;
            if (string.Equals(key, _selectedDeviceKey, StringComparison.OrdinalIgnoreCase)) return;
            var resp = await IpcClient.SendAsync("setSelectedController", key);
            if (!resp.IsOk)
                StatusText.Text = resp.Error ?? "Select failed";
            else
            {
                if (!string.Equals(key, _sharedEditDecisionDeviceKey, StringComparison.OrdinalIgnoreCase))
                {
                    _sharedEditDecision = SharedEditDecision.None;
                    _sharedEditDecisionDeviceKey = "";
                }
                await RefreshAsync(force: true);
            }
        }

        void RebuildControllerStrips(bool force)
        {
            var key = string.Join("|", _controllers
                .OrderBy(c => c.Order)
                .Select(c => c.Order + ":" + c.DeviceKey + ":" + c.Model + ":" + (c.DisplayName ?? "") + ":" + (c.Connected ? "1" : "0") + ":" + (c.HasProfileOverride ? "1" : "0")))
                + "#" + _selectedDeviceKey;
            if (!force && key == _lastControllersKey) return;
            _lastControllersKey = key;

            var show = _controllers.Count > 0;
            ViewControllerStrip.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            EditControllerStrip.Visibility = show ? Visibility.Visible : Visibility.Collapsed;

            FillControllerTabs(ViewControllerTabs, compact: true);
            FillControllerTabs(EditControllerTabs, compact: false);
            if (SettingsView.Visibility == Visibility.Visible)
                RebuildSettingsControllerList();
        }

        void FillControllerTabs(StackPanel panel, bool compact)
        {
            panel.Children.Clear();
            foreach (var pad in _controllers.OrderBy(c => c.Order))
            {
                var selected = string.Equals(pad.DeviceKey, _selectedDeviceKey, StringComparison.OrdinalIgnoreCase);
                var label = (pad.Order + 1) + ". " + ShortPadName(pad);
                var btn = new Button
                {
                    Content = label,
                    Tag = pad.DeviceKey,
                    Style = (Style)Application.Current.Resources[selected ? "PillAccentButtonStyle" : "PillButtonStyle"],
                    Padding = compact ? new Thickness(12, 8, 12, 8) : new Thickness(14, 10, 14, 10),
                    FontSize = compact ? 12 : 13,
                    Margin = new Thickness(0, 0, 8, 0)
                };
                btn.Click += ControllerTab_Click;
                panel.Children.Add(btn);
            }
        }

        static string ShortPadName(ControllerPadInfo pad)
        {
            if (!string.IsNullOrWhiteSpace(pad.DisplayName))
            {
                var custom = pad.DisplayName.Trim();
                // Keep stock model labels short in the strip.
                if (custom.Equals("Steam Controller 2", StringComparison.OrdinalIgnoreCase))
                    return "SC2";
                if (custom.Equals("Steam Controller", StringComparison.OrdinalIgnoreCase))
                    return pad.Model == "sc1" ? "SC1" : "SC";
                // Custom names: show up to ~14 chars.
                return custom.Length <= 14 ? custom : custom.Substring(0, 13) + "…";
            }
            return pad.Model == "sc1" ? "SC1" : (pad.Model == "sc2" ? "SC2" : "Pad");
        }

        async void SaveAs_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_activeProfileId)) return;
            var name = await PromptTextAsync("Save As", "Name for this layout", _selectedProfileName);
            if (string.IsNullOrWhiteSpace(name)) return;
            var resp = await IpcClient.SendAsync("saveAsProfile", _activeProfileId + "\t" + name.Trim());
            StatusText.Text = resp.IsOk ? "Saved as " + name.Trim() : (resp.Error ?? "Save As failed");
            await RefreshAsync(force: true);
        }

        void ChangeLayout_Click(object sender, RoutedEventArgs e)
        {
            _browseSection = "Templates";
            MainView.Visibility = Visibility.Collapsed;
            BrowseLayoutView.Visibility = Visibility.Visible;
            RebuildBrowseLayoutList();
            HighlightBrowseTabs();
        }

        void BrowseLayoutBack_Click(object sender, RoutedEventArgs e)
        {
            BrowseLayoutView.Visibility = Visibility.Collapsed;
            MainView.Visibility = Visibility.Visible;
        }

        void BrowseSection_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button btn) || !(btn.Tag is string section)) return;
            _browseSection = section;
            RebuildBrowseLayoutList();
            HighlightBrowseTabs();
        }

        void CycleBrowseSection(int delta)
        {
            if (BrowseLayoutView.Visibility != Visibility.Visible) return;
            _browseSection = _browseSection == "Templates" ? "Yours" : "Templates";
            RebuildBrowseLayoutList();
            HighlightBrowseTabs();
        }

        void HighlightBrowseTabs()
        {
            SetTabActive(BrowseTabTemplates, _browseSection == "Templates");
            SetTabActive(BrowseTabYours, _browseSection == "Yours");
        }

        void RebuildBrowseLayoutList()
        {
            BrowseLayoutList.Children.Clear();
            if (_browseSection == "Templates")
            {
                BrowseLayoutList.Children.Add(new TextBlock
                {
                    Text = "Templates",
                    FontSize = 14,
                    FontWeight = Windows.UI.Text.FontWeights.SemiBold,
                    Margin = new Thickness(0, 0, 0, 4)
                });
                BrowseLayoutList.Children.Add(new TextBlock
                {
                    Text = "Official layouts — preview, then Apply to your current layout.",
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 8),
                    Foreground = (Windows.UI.Xaml.Media.SolidColorBrush)Application.Current.Resources["SystemControlForegroundBaseMediumBrush"]
                });

                foreach (var layout in _officialLayouts)
                {
                    var btn = new Button
                    {
                        Tag = layout.Id,
                        Style = (Style)Application.Current.Resources["PillButtonStyle"],
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        HorizontalContentAlignment = HorizontalAlignment.Left,
                        Padding = new Thickness(18, 14, 18, 14),
                        Margin = new Thickness(0, 0, 0, 10)
                    };
                    var panel = new StackPanel();
                    panel.Children.Add(new TextBlock { Text = layout.Name, FontSize = 15, FontWeight = Windows.UI.Text.FontWeights.SemiBold });
                    panel.Children.Add(new TextBlock
                    {
                        Text = layout.Description,
                        FontSize = 12,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 4, 0, 0),
                        Foreground = (Windows.UI.Xaml.Media.SolidColorBrush)Application.Current.Resources["SystemControlForegroundBaseMediumBrush"]
                    });
                    btn.Content = panel;
                    btn.Click += async (s, _) =>
                    {
                        if (s is Button b && b.Tag is string id)
                            await OpenPreviewLayoutAsync(id);
                    };
                    BrowseLayoutList.Children.Add(btn);
                }
            }
            else
            {
                BrowseLayoutList.Children.Add(new TextBlock
                {
                    Text = "Your layouts",
                    FontSize = 14,
                    FontWeight = Windows.UI.Text.FontWeights.SemiBold,
                    Margin = new Thickness(0, 0, 0, 4)
                });
                BrowseLayoutList.Children.Add(new TextBlock
                {
                    Text = "Saved layouts — select to switch immediately.",
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 8),
                    Foreground = (Windows.UI.Xaml.Media.SolidColorBrush)Application.Current.Resources["SystemControlForegroundBaseMediumBrush"]
                });

                if (_profileNames.Count == 0)
                {
                    BrowseLayoutList.Children.Add(new TextBlock
                    {
                        Text = "No saved layouts yet. Apply a template, then Save As…",
                        FontSize = 12,
                        TextWrapping = TextWrapping.Wrap
                    });
                    return;
                }

                foreach (var name in _profileNames)
                {
                    var btn = new Button
                    {
                        Content = name,
                        Tag = name,
                        Style = (Style)Application.Current.Resources["PillButtonStyle"],
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        HorizontalContentAlignment = HorizontalAlignment.Left,
                        Padding = new Thickness(18, 14, 18, 14),
                        Margin = new Thickness(0, 0, 0, 10)
                    };
                    if (string.Equals(name, _selectedProfileName, StringComparison.OrdinalIgnoreCase))
                        btn.Style = (Style)Application.Current.Resources["PillAccentButtonStyle"];
                    btn.Click += async (s, _) =>
                    {
                        if (!(s is Button b) || !(b.Tag is string profileName)) return;
                        var resp = await IpcClient.SendAsync("setActiveProfileByName", profileName);
                        StatusText.Text = resp.IsOk ? "Switched to " + profileName : (resp.Error ?? "Switch failed");
                        BrowseLayoutView.Visibility = Visibility.Collapsed;
                        MainView.Visibility = Visibility.Visible;
                        await RefreshAsync(force: true);
                    };
                    BrowseLayoutList.Children.Add(btn);
                }
            }
        }

        async Task OpenPreviewLayoutAsync(string layoutId)
        {
            var layout = _officialLayouts.FirstOrDefault(l => l.Id == layoutId);
            if (layout.Id == null)
            {
                StatusText.Text = "Unknown layout";
                return;
            }

            var resp = await IpcClient.SendAsync("previewLayout", layoutId);
            if (!resp.IsOk || string.IsNullOrWhiteSpace(resp.Payload))
            {
                StatusText.Text = resp.Error ?? "Preview failed";
                return;
            }

            if (!JsonObject.TryParse(resp.Payload, out var preview) || preview == null)
            {
                StatusText.Text = "Invalid preview payload";
                return;
            }

            _previewLayoutId = layoutId;
            PreviewLayoutTitle.Text = layout.Name;
            PreviewLayoutSubtitle.Text = layout.Description;

            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (preview.ContainsKey("inputMap"))
            {
                var obj = preview.GetNamedObject("inputMap");
                foreach (var key in obj.Keys)
                    map[key] = obj.GetNamedString(key);
            }

            BuildPreviewLabels(map);
            BrowseLayoutView.Visibility = Visibility.Collapsed;
            PreviewLayoutView.Visibility = Visibility.Visible;
        }

        void BuildPreviewLabels(Dictionary<string, string> map)
        {
            PreviewCategorySummaries.Children.Clear();

            foreach (var cat in EditCategories)
            {
                if (cat.InputIds.Length == 0)
                    continue;

                PreviewCategorySummaries.Children.Add(new TextBlock
                {
                    Text = cat.Name,
                    FontSize = 12,
                    FontWeight = Windows.UI.Text.FontWeights.SemiBold,
                    Margin = new Thickness(0, 10, 0, 6),
                    Foreground = (Windows.UI.Xaml.Media.SolidColorBrush)Application.Current.Resources["SystemControlForegroundBaseMediumBrush"]
                });

                foreach (var id in cat.InputIds)
                {
                    var mapped = map.ContainsKey(id) ? map[id] : "None";
                    if (string.IsNullOrEmpty(mapped)) mapped = "None";

                    var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    var nameBlock = new TextBlock
                    {
                        Text = GetInputLabel(id),
                        FontSize = 14,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    var valueBlock = new TextBlock
                    {
                        Text = mapped,
                        FontSize = 14,
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Opacity = 0.75
                    };
                    Grid.SetColumn(valueBlock, 1);
                    grid.Children.Add(nameBlock);
                    grid.Children.Add(valueBlock);
                    PreviewCategorySummaries.Children.Add(new Border
                    {
                        Child = grid,
                        Background = (Windows.UI.Xaml.Media.Brush)Application.Current.Resources["SystemControlBackgroundBaseLowBrush"],
                        CornerRadius = new CornerRadius(12),
                        Padding = new Thickness(14, 12, 14, 12),
                        Margin = new Thickness(0, 0, 0, 6)
                    });
                }
            }
        }

        void PreviewLayoutBack_Click(object sender, RoutedEventArgs e)
        {
            PreviewLayoutView.Visibility = Visibility.Collapsed;
            BrowseLayoutView.Visibility = Visibility.Visible;
        }

        async void ApplyLayout_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_activeProfileId) || string.IsNullOrEmpty(_previewLayoutId))
                return;
            if (!await EnsureSharedBindingsEditAllowedAsync())
                return;
            var resp = await IpcClient.SendAsync("applyLayout", _activeProfileId + "\t" + _previewLayoutId);
            StatusText.Text = resp.IsOk
                ? "Applied " + (_officialLayouts.FirstOrDefault(l => l.Id == _previewLayoutId).Name ?? _previewLayoutId)
                : (resp.Error ?? "Apply failed");
            PreviewLayoutView.Visibility = Visibility.Collapsed;
            BrowseLayoutView.Visibility = Visibility.Collapsed;
            MainView.Visibility = Visibility.Visible;
            SetViewEditTab("Edit");
            await RefreshAsync(force: true);
        }

        void WidgetPage_PreviewKeyDown(object sender, Windows.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            // Don't steal keys while typing in a dialog text box
            if (FocusManager.GetFocusedElement() is TextBox)
                return;

            var key = e.Key;
            if (ListPickerView.Visibility == Visibility.Visible)
            {
                if (key == Windows.System.VirtualKey.GamepadB || key == Windows.System.VirtualKey.Escape)
                {
                    ListPickerBack_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                }
                return;
            }

            if (AdvancedSettingsView.Visibility == Visibility.Visible)
            {
                // Don't bind A globally — it must toggle pill rows. Save is the accent button.
                if (key == Windows.System.VirtualKey.GamepadB || key == Windows.System.VirtualKey.Escape)
                {
                    AdvancedSettingsBack_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                }
                return;
            }

            if (SettingsView.Visibility == Visibility.Visible)
            {
                if (key == Windows.System.VirtualKey.GamepadB || key == Windows.System.VirtualKey.Escape)
                {
                    SettingsBack_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                }
                return;
            }

            if (RemapView.Visibility == Visibility.Visible)
            {
                if (key == Windows.System.VirtualKey.GamepadX)
                { CycleRemapTab(-1); e.Handled = true; }
                else if (key == Windows.System.VirtualKey.GamepadB)
                { CycleRemapTab(1); e.Handled = true; }
                else if (key == Windows.System.VirtualKey.Escape)
                {
                    RemapCancel_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                }
                return;
            }

            if (PreviewLayoutView.Visibility == Visibility.Visible)
            {
                if (key == Windows.System.VirtualKey.GamepadA || key == Windows.System.VirtualKey.Enter)
                {
                    ApplyLayout_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                }
                else if (key == Windows.System.VirtualKey.GamepadB || key == Windows.System.VirtualKey.Escape)
                {
                    PreviewLayoutBack_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                }
                return;
            }

            if (BrowseLayoutView.Visibility == Visibility.Visible)
            {
                if (key == Windows.System.VirtualKey.GamepadB)
                { CycleBrowseSection(1); e.Handled = true; }
                else if (key == Windows.System.VirtualKey.GamepadX)
                { CycleBrowseSection(-1); e.Handled = true; }
                else if (key == Windows.System.VirtualKey.Escape)
                {
                    BrowseLayoutBack_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                }
                return;
            }

            // Y / B / X / triggers are handled once in CoreWindow_KeyDown (avoid double-step).
        }

        async void ModePicker_Click(object sender, RoutedEventArgs e)
        {
            if (_suppress || string.IsNullOrEmpty(_activeProfileId)) return;
            if (!(sender is Button btn) || !(btn.Tag is string tag)) return;

            var modes = tag == "gyro" ? GyroModes : TrackpadModes;
            var labels = modes.Select(FormatModeLabel).ToArray();
            var current = _modeValues.ContainsKey(tag) ? _modeValues[tag] : "Off";
            var title = tag == "gyro" ? "Gyro mode" : (tag == "left" ? "Left pad mode" : "Right pad mode");
            var selected = await PickListIndexAsync(title, labels, FormatModeLabel(current));
            if (AdvancedSettingsView.Visibility != Visibility.Visible)
                MainView.Visibility = Visibility.Visible;
            if (selected < 0) return;

            var mode = modes[selected];
            if (string.Equals(mode, current, StringComparison.OrdinalIgnoreCase))
                return;

            _modeValues[tag] = mode;
            if (btn.Content is StackPanel sp && sp.Children.Count > 1 && sp.Children[1] is TextBlock valueLabel)
                valueLabel.Text = FormatModeLabel(mode);

            if (!await EnsureSharedBindingsEditAllowedAsync())
            {
                // Revert local UI if the user cancelled.
                _modeValues[tag] = current;
                if (btn.Content is StackPanel sp2 && sp2.Children.Count > 1 && sp2.Children[1] is TextBlock valueLabel2)
                    valueLabel2.Text = FormatModeLabel(current);
                return;
            }

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

        /// <summary>In-widget list picker with pill rows (ContentDialog chrome looks wrong in Game Bar).</summary>
        async Task<int> PickListIndexAsync(string title, IReadOnlyList<string> items, string selectedValue)
        {
            var selectedIndex = -1;
            for (int i = 0; i < items.Count; i++)
            {
                if (string.Equals(items[i], selectedValue, StringComparison.OrdinalIgnoreCase))
                {
                    selectedIndex = i;
                    break;
                }
            }
            return await PickListIndexAsync(title, items, selectedIndex);
        }

        async Task<int> PickListIndexAsync(string title, IReadOnlyList<string> items, int selectedIndex)
        {
            if (items == null || items.Count == 0) return -1;

            _listPickerTcs?.TrySetResult(-1);
            _listPickerTcs = new TaskCompletionSource<int>();

            ListPickerTitle.Text = title;
            ListPickerContent.Children.Clear();
            for (int i = 0; i < items.Count; i++)
            {
                var index = i;
                var selected = index == selectedIndex
                               || (selectedIndex < 0 && index == 0);
                var btn = new ToggleButton
                {
                    Content = items[i],
                    IsChecked = selected,
                    Style = (Style)Application.Current.Resources["PillToggleButtonStyle"],
                    Margin = new Thickness(0, 0, 0, 8),
                    Tag = index
                };
                btn.Click += (s, __) =>
                {
                    _listPickerTcs?.TrySetResult(index);
                    CloseListPicker();
                };
                ListPickerContent.Children.Add(btn);
            }

            MainView.Visibility = Visibility.Collapsed;
            ListPickerView.Visibility = Visibility.Visible;
            return await _listPickerTcs.Task;
        }

        void ListPickerBack_Click(object sender, RoutedEventArgs e)
        {
            _listPickerTcs?.TrySetResult(-1);
            CloseListPicker();
            if (AdvancedSettingsView.Visibility != Visibility.Visible
                && SettingsView.Visibility != Visibility.Visible
                && RemapView.Visibility != Visibility.Visible
                && BrowseLayoutView.Visibility != Visibility.Visible
                && PreviewLayoutView.Visibility != Visibility.Visible)
                MainView.Visibility = Visibility.Visible;
        }

        void CloseListPicker()
        {
            ListPickerView.Visibility = Visibility.Collapsed;
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

        // ═══════════════ Sensitivity / Invert IPC ═══════════════

        async Task SendSensitivityAsync()
        {
            if (string.IsNullOrEmpty(_activeProfileId)) return;
            if (!await EnsureSharedBindingsEditAllowedAsync())
                return;
            var parts = new List<string> { "\"profileId\":\"" + _activeProfileId + "\"" };
            foreach (var kv in _sensValues)
                parts.Add("\"" + kv.Key + "\":" + kv.Value.ToString(CultureInfo.InvariantCulture));
            foreach (var kv in _invertValues)
                parts.Add("\"" + kv.Key + "\":" + (kv.Value ? "true" : "false"));
            parts.Add("\"gyroButtonMode\":\"" + _gyroButtonMode + "\"");
            parts.Add("\"gyroButtonCombine\":\"" + _gyroButtonCombine + "\"");
            parts.Add("\"gyroButtons\":[" + string.Join(",", _gyroButtons.Select(b => "\"" + b + "\"")) + "]");
            parts.Add("\"leftTrackpadSettings\":" + SerializePadSettings("left"));
            parts.Add("\"rightTrackpadSettings\":" + SerializePadSettings("right"));
            var json = "{" + string.Join(",", parts) + "}";
            var resp = await IpcClient.SendAsync("setSensitivity", json);
            if (!resp.IsOk)
                StatusText.Text = resp.Error ?? "Sensitivity update failed";
        }

        string SerializePadSettings(string side) =>
            "{"
            + "\"trackballMode\":" + (_trackballMode[side] ? "true" : "false") + ","
            + "\"trackballFriction\":\"" + _trackballFriction[side] + "\","
            + "\"verticalFrictionScale\":" + _padVertFriction[side].ToString(CultureInfo.InvariantCulture) + ","
            + "\"smoothing\":" + _padSmoothing[side].ToString(CultureInfo.InvariantCulture) + ","
            + "\"rotationDegrees\":" + _padRotation[side].ToString(CultureInfo.InvariantCulture)
            + "}";

        async void AdvancedSettingsCog_Click(object sender, RoutedEventArgs e)
        {
            if (_suppress || !(sender is Button btn) || !(btn.Tag is string tag)) return;
            if (tag == "gyro")
                await ShowGyroAdvancedSettingsAsync();
            else
                await ShowTrackpadAdvancedSettingsAsync(tag);
        }

        async Task ShowGyroAdvancedSettingsAsync()
        {
            var draftButtons = new List<string>(_gyroButtons);
            var draftMode = _gyroButtonMode;
            var draftCombine = _gyroButtonCombine;
            var draftDots = _sensValues.ContainsKey("gyroDotsPer360") ? _sensValues["gyroDotsPer360"] : 6545;

            AdvancedSettingsContent.Children.Clear();
            AdvancedSettingsTitle.Text = "Gyro settings";
            AdvancedSettingsSubtitle.Text = "B cancel · Save when done";

            AdvancedSettingsContent.Children.Add(SectionLabel("Activation"));
            AdvancedSettingsContent.Children.Add(Hint("Buttons Enable / Suppress / Toggle the gyro. None = always on."));

            var chipsHost = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };
            void RebuildChips()
            {
                chipsHost.Children.Clear();
                if (draftButtons.Count == 0)
                {
                    chipsHost.Children.Add(new Border
                    {
                        CornerRadius = new CornerRadius(16),
                        Background = (Windows.UI.Xaml.Media.Brush)Application.Current.Resources["SystemControlBackgroundBaseLowBrush"],
                        Padding = new Thickness(16, 12, 16, 12),
                        Margin = new Thickness(0, 0, 0, 8),
                        Child = new TextBlock
                        {
                            Text = "Always on",
                            FontSize = 15,
                            Foreground = (Windows.UI.Xaml.Media.SolidColorBrush)Application.Current.Resources["SystemControlForegroundBaseMediumBrush"]
                        }
                    });
                }
                else
                {
                    foreach (var id in draftButtons.ToList())
                    {
                        var label = GyroButtonLabel(id);
                        var chip = new Button
                        {
                            Content = label + "   \u2715",
                            Style = (Style)Application.Current.Resources["PillButtonStyle"],
                            HorizontalAlignment = HorizontalAlignment.Stretch,
                            HorizontalContentAlignment = HorizontalAlignment.Left,
                            Margin = new Thickness(0, 0, 0, 8),
                            Padding = new Thickness(16, 12, 16, 12),
                            Tag = id
                        };
                        ToolTipService.SetToolTip(chip, "Remove " + label);
                        chip.Click += (_, __) =>
                        {
                            draftButtons.RemoveAll(b => b.Equals(id, StringComparison.OrdinalIgnoreCase));
                            RebuildChips();
                        };
                        chipsHost.Children.Add(chip);
                    }
                }

                var addBtn = new Button
                {
                    Content = "+ Add button",
                    Style = (Style)Application.Current.Resources["PillAccentButtonStyle"],
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Margin = new Thickness(0, 4, 0, 0),
                    Padding = new Thickness(16, 12, 16, 12)
                };
                addBtn.Click += async (_, __) =>
                {
                    var available = GyroActivationChoices
                        .Where(c => !draftButtons.Any(b => b.Equals(c.Id, StringComparison.OrdinalIgnoreCase)))
                        .ToList();
                    if (available.Count == 0)
                    {
                        StatusText.Text = "All activation buttons already added";
                        return;
                    }

                    AdvancedSettingsView.Visibility = Visibility.Collapsed;
                    var picked = await PickListIndexAsync(
                        "Add gyro button",
                        available.Select(c => c.Label).ToArray(),
                        0);
                    AdvancedSettingsView.Visibility = Visibility.Visible;
                    MainView.Visibility = Visibility.Collapsed;
                    if (picked < 0) return;
                    draftButtons.Add(available[picked].Id);
                    RebuildChips();
                };
                chipsHost.Children.Add(addBtn);

                if (draftButtons.Count > 0)
                {
                    var clearBtn = new Button
                    {
                        Content = "Clear (always on)",
                        Style = (Style)Application.Current.Resources["PillButtonStyle"],
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        Margin = new Thickness(0, 8, 0, 0),
                        Padding = new Thickness(16, 12, 16, 12)
                    };
                    clearBtn.Click += (_, __) =>
                    {
                        draftButtons.Clear();
                        RebuildChips();
                    };
                    chipsHost.Children.Add(clearBtn);
                }
            }
            RebuildChips();
            AdvancedSettingsContent.Children.Add(chipsHost);

            AdvancedSettingsContent.Children.Add(SectionLabel("Behavior"));
            AdvancedSettingsContent.Children.Add(BuildChoicePillRow(
                "Enable / Suppress / Toggle",
                GyroButtonModes, GyroButtonModes.Select(FormatModeLabel).ToArray(),
                draftMode, v => draftMode = v));
            AdvancedSettingsContent.Children.Add(BuildChoicePillRow(
                "Multiple buttons",
                GyroCombineModes, GyroCombineModes.Select(FormatModeLabel).ToArray(),
                draftCombine, v => draftCombine = v));

            AdvancedSettingsContent.Children.Add(SectionLabel("Calibration"));
            AdvancedSettingsContent.Children.Add(Hint("Dots Per 360° — one full turn → this many mouse pixels at 1× sensitivity."));
            var dots = MakeStyledSlider("Dots Per 360°", 1000, 12000, 5, draftDots);
            dots.ValueChanged += (_, ev) => draftDots = ev.NewValue;
            AdvancedSettingsContent.Children.Add(dots);

            if (!await ShowAdvancedSettingsSheetAsync())
                return;

            _gyroButtons.Clear();
            _gyroButtons.AddRange(draftButtons);
            _gyroButtonMode = draftMode;
            _gyroButtonCombine = draftCombine;
            _sensValues["gyroDotsPer360"] = draftDots;
            await SendSensitivityAsync();
            RebuildEditCategoryContent();
        }

        async Task ShowTrackpadAdvancedSettingsAsync(string side)
        {
            var draftTrackball = _trackballMode[side];
            var draftFriction = _trackballFriction[side];
            var draftVert = _padVertFriction[side];
            var draftSmooth = _padSmoothing[side];
            var draftRot = _padRotation[side];

            AdvancedSettingsContent.Children.Clear();
            AdvancedSettingsTitle.Text = side == "left" ? "Left trackpad" : "Right trackpad";
            AdvancedSettingsSubtitle.Text = "B cancel · Save when done";

            AdvancedSettingsContent.Children.Add(SectionLabel("Trackball"));
            var trackball = MakePillToggle("Trackball Mode", draftTrackball);
            trackball.Checked += (_, __) => draftTrackball = true;
            trackball.Unchecked += (_, __) => draftTrackball = false;
            AdvancedSettingsContent.Children.Add(trackball);
            AdvancedSettingsContent.Children.Add(Hint("Keeps momentum after you lift your thumb."));

            AdvancedSettingsContent.Children.Add(BuildChoicePillRow(
                "Trackball Friction",
                TrackballFrictions, TrackballFrictions.Select(FormatModeLabel).ToArray(),
                draftFriction, v => draftFriction = v));

            AdvancedSettingsContent.Children.Add(SectionLabel("Feel"));
            var vFric = MakeStyledSlider("Vertical Friction Scale %", 10, 300, 5, draftVert * 100);
            vFric.ValueChanged += (_, ev) => draftVert = ev.NewValue / 100.0;
            AdvancedSettingsContent.Children.Add(vFric);
            AdvancedSettingsContent.Children.Add(Hint("Higher stops up/down flicks sooner (good for camera yaw)."));

            var smooth = MakeStyledSlider("Smoothing", 0, 100, 1, draftSmooth);
            smooth.ValueChanged += (_, ev) => draftSmooth = ev.NewValue;
            AdvancedSettingsContent.Children.Add(smooth);
            AdvancedSettingsContent.Children.Add(Hint("Higher removes jitter but adds lag."));

            var rot = MakeStyledSlider("Rotation (°)", -45, 45, 1, draftRot);
            rot.ValueChanged += (_, ev) => draftRot = ev.NewValue;
            AdvancedSettingsContent.Children.Add(rot);
            AdvancedSettingsContent.Children.Add(Hint("Cant pad axes to match a natural thumb swipe."));

            if (!await ShowAdvancedSettingsSheetAsync())
                return;

            _trackballMode[side] = draftTrackball;
            _trackballFriction[side] = draftFriction;
            _padVertFriction[side] = draftVert;
            _padSmoothing[side] = draftSmooth;
            _padRotation[side] = draftRot;
            await SendSensitivityAsync();
        }

        async Task<bool> ShowAdvancedSettingsSheetAsync()
        {
            _advancedSettingsTcs?.TrySetResult(false);
            _advancedSettingsTcs = new TaskCompletionSource<bool>();
            MainView.Visibility = Visibility.Collapsed;
            AdvancedSettingsView.Visibility = Visibility.Visible;
            return await _advancedSettingsTcs.Task;
        }

        void AdvancedSettingsBack_Click(object sender, RoutedEventArgs e)
        {
            AdvancedSettingsView.Visibility = Visibility.Collapsed;
            MainView.Visibility = Visibility.Visible;
            _advancedSettingsTcs?.TrySetResult(false);
        }

        void AdvancedSettingsSave_Click(object sender, RoutedEventArgs e)
        {
            AdvancedSettingsView.Visibility = Visibility.Collapsed;
            MainView.Visibility = Visibility.Visible;
            _advancedSettingsTcs?.TrySetResult(true);
        }

        /// <summary>Pill row that opens the list picker sheet (same chrome as the rest of the widget).</summary>
        UIElement BuildChoicePillRow(string label, string[] values, string[] displayLabels,
            string current, Action<string> onChanged)
        {
            var currentLocal = current;
            var btn = new Button
            {
                Style = (Style)Application.Current.Resources["PillButtonStyle"],
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 8),
                Padding = new Thickness(16, 12, 16, 12)
            };
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 12,
                Foreground = (Windows.UI.Xaml.Media.SolidColorBrush)Application.Current.Resources["SystemControlForegroundBaseMediumBrush"]
            });
            var valueText = new TextBlock { Text = FormatModeLabel(currentLocal), FontSize = 15 };
            panel.Children.Add(valueText);
            btn.Content = panel;

            btn.Click += async (_, __) =>
            {
                var idx = Array.IndexOf(values, currentLocal);
                AdvancedSettingsView.Visibility = Visibility.Collapsed;
                var picked = await PickListIndexAsync(label, displayLabels, idx >= 0 ? idx : 0);
                AdvancedSettingsView.Visibility = Visibility.Visible;
                MainView.Visibility = Visibility.Collapsed;
                if (picked < 0) return;
                currentLocal = values[picked];
                onChanged(currentLocal);
                valueText.Text = displayLabels[picked];
            };
            return btn;
        }

        static string GyroButtonLabel(string id)
        {
            foreach (var (choiceId, label) in GyroActivationChoices)
            {
                if (choiceId.Equals(id, StringComparison.OrdinalIgnoreCase))
                    return label;
            }
            return id;
        }

        static ToggleButton MakePillToggle(string label, bool isOn)
        {
            return new ToggleButton
            {
                Content = label,
                IsChecked = isOn,
                Style = (Style)Application.Current.Resources["PillToggleButtonStyle"],
                Margin = new Thickness(0, 0, 0, 8)
            };
        }

        static Slider MakeStyledSlider(string header, double min, double max, double step, double value)
        {
            return new Slider
            {
                Header = header,
                Minimum = min,
                Maximum = max,
                StepFrequency = step,
                SmallChange = step,
                LargeChange = step * 5,
                SnapsTo = SliderSnapsTo.StepValues,
                Value = value,
                MinHeight = 48,
                Margin = new Thickness(0, 0, 0, 8)
            };
        }

        static TextBlock SectionLabel(string text) => new TextBlock
        {
            Text = text,
            FontSize = 13,
            FontWeight = Windows.UI.Text.FontWeights.SemiBold,
            Foreground = (Windows.UI.Xaml.Media.SolidColorBrush)Application.Current.Resources["SystemControlForegroundBaseMediumBrush"],
            Margin = new Thickness(0, 12, 0, 8)
        };

        static TextBlock Hint(string text) => new TextBlock
        {
            Text = text,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Windows.UI.Xaml.Media.SolidColorBrush)Application.Current.Resources["SystemControlForegroundBaseMediumBrush"],
            Margin = new Thickness(0, 0, 0, 8)
        };

        string FormatGyroButtonsSummary()
        {
            if (_gyroButtons.Count == 0) return "Always on";
            if (_gyroButtons.Count <= 2) return string.Join(", ", _gyroButtons);
            return _gyroButtons.Count + " selected";
        }

        void ReadPadSettings(Windows.Data.Json.JsonObject state, string key, string side)
        {
            if (!state.ContainsKey(key)) return;
            var obj = state.GetNamedObject(key);
            _trackballMode[side] = obj.GetNamedBoolean("trackballMode", true);
            _trackballFriction[side] = obj.GetNamedString("trackballFriction", "Medium");
            _padVertFriction[side] = obj.GetNamedNumber("verticalFrictionScale", 1);
            _padSmoothing[side] = obj.GetNamedNumber("smoothing", 20);
            _padRotation[side] = obj.GetNamedNumber("rotationDegrees", 0);
        }

        // ═══════════════ Edit panel: category tabs + content ═══════════════

        void RebuildEditCategoryTabs()
        {
            EditCategoryTabs.Children.Clear();
            foreach (var cat in EditCategories)
            {
                var btn = new Button
                {
                    Content = cat.Name,
                    Tag = cat.Name,
                    Style = (Style)Application.Current.Resources["PillButtonStyle"],
                    Padding = new Thickness(16, 10, 16, 10),
                    Margin = new Thickness(0, 0, 8, 0),
                    FontSize = 13,
                    MinWidth = 0
                };
                btn.Click += EditCategoryTab_Click;
                SetTabActive(btn, cat.Name == _activeEditCategory);
                EditCategoryTabs.Children.Add(btn);
            }
            RebuildEditCategoryContent();
            // Layout must complete before we can scroll the active tab into view.
            _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, BringActiveCategoryTabIntoView);
        }

        void EditCategoryTab_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button btn) || !(btn.Tag is string cat)) return;
            _activeEditCategory = cat;
            foreach (var child in EditCategoryTabs.Children)
            {
                if (child is Button b)
                    SetTabActive(b, (b.Tag as string) == _activeEditCategory);
            }
            RebuildEditCategoryContent();
            BringActiveCategoryTabIntoView();
        }

        void RebuildEditCategoryContent()
        {
            var wasSuppress = _suppress;
            _suppress = true;
            try
            {
                RebuildEditCategoryContentCore();
            }
            finally
            {
                _suppress = wasSuppress;
            }
        }

        void RebuildEditCategoryContentCore()
        {
            EditCategoryContent.Children.Clear();
            var category = EditCategories.FirstOrDefault(c => c.Name == _activeEditCategory);
            if (category.Name == null) return;

            // Input cards
            if (category.InputIds.Length > 0)
            {
                foreach (var inputId in category.InputIds)
                    AddInputCard(inputId);
            }

            // Mode pickers (trackpad/gyro) — behavior dropdown + Steam-style cog for advanced settings
            if (category.ModeKeys.Length > 0)
            {
                AddSectionHeader("Behavior");
                foreach (var modeKey in category.ModeKeys)
                {
                    var label = modeKey == "gyro" ? "Gyro" : (modeKey == "left" ? "Left pad" : "Right pad");
                    var current = _modeValues.ContainsKey(modeKey) ? _modeValues[modeKey] : "Off";

                    var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    var btn = new Button
                    {
                        Tag = modeKey,
                        Style = (Style)Application.Current.Resources["PillButtonStyle"],
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        HorizontalContentAlignment = HorizontalAlignment.Left,
                        Padding = new Thickness(16, 12, 16, 12),
                        Margin = new Thickness(0, 0, 8, 0)
                    };
                    var panel = new StackPanel();
                    panel.Children.Add(new TextBlock
                    {
                        Text = label,
                        FontSize = 12,
                        Foreground = (Windows.UI.Xaml.Media.SolidColorBrush)Application.Current.Resources["SystemControlForegroundBaseMediumBrush"]
                    });
                    panel.Children.Add(new TextBlock { Text = FormatModeLabel(current), FontSize = 15 });
                    btn.Content = panel;
                    btn.Click += ModePicker_Click;
                    Grid.SetColumn(btn, 0);
                    row.Children.Add(btn);

                    var cog = new Button
                    {
                        Content = "\u2699",
                        Tag = modeKey,
                        FontSize = 20,
                        Width = 48,
                        Height = 48,
                        Padding = new Thickness(0),
                        Style = (Style)Application.Current.Resources["PillButtonStyle"],
                        VerticalAlignment = VerticalAlignment.Stretch
                    };
                    ToolTipService.SetToolTip(cog, "Advanced settings");
                    cog.Click += AdvancedSettingsCog_Click;
                    Grid.SetColumn(cog, 1);
                    row.Children.Add(cog);

                    EditCategoryContent.Children.Add(row);
                }
            }

            // Sensitivity sliders
            if (category.SensKeys.Length > 0)
            {
                AddSectionHeader("Sensitivity");
                EditCategoryContent.Children.Add(new TextBlock
                {
                    Text = "Axis scale for both sides (not left/right).",
                    FontSize = 11,
                    Foreground = (Windows.UI.Xaml.Media.SolidColorBrush)Application.Current.Resources["SystemControlForegroundBaseMediumBrush"],
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 4)
                });
                foreach (var key in category.SensKeys)
                {
                    var label = FormatSettingLabel(key);
                    var slider = new Slider
                    {
                        Header = label,
                        Minimum = 0.1,
                        Maximum = 3,
                        StepFrequency = 0.1,
                        SmallChange = 0.1,
                        LargeChange = 0.5,
                        SnapsTo = SliderSnapsTo.StepValues,
                        Value = _sensValues.ContainsKey(key) ? _sensValues[key] : 1.0,
                        Tag = key,
                        FontSize = 14,
                        MinHeight = 48,
                        Margin = new Thickness(0, 0, 0, 10)
                    };
                    slider.ValueChanged += (s, ev) =>
                    {
                        if (_suppress) return;
                        _sensValues[key] = ev.NewValue;
                        if (!_sensitivityThrottle) { _sensitivityThrottle = true; _ = ThrottledSendSensitivity(); }
                    };
                    EditCategoryContent.Children.Add(slider);
                }
            }

            // Deadzone sliders — use 0–50 integer % so UWP doesn't snap 0↔max
            // (fractional Maximum with default SmallChange=1 only allows endpoints).
            if (category.DeadzoneKeys.Length > 0)
            {
                AddSectionHeader("Deadzone");
                foreach (var key in category.DeadzoneKeys)
                {
                    var label = FormatSettingLabel(key);
                    var current = _sensValues.ContainsKey(key) ? _sensValues[key] : 0.05;
                    var pct = Math.Max(0, Math.Min(50, Math.Round(current * 100.0)));
                    var slider = new Slider
                    {
                        Header = label + " (%)",
                        Minimum = 0,
                        Maximum = 50,
                        StepFrequency = 1,
                        SmallChange = 1,
                        LargeChange = 5,
                        SnapsTo = SliderSnapsTo.StepValues,
                        Value = pct,
                        Tag = key,
                        FontSize = 14,
                        MinHeight = 48,
                        Margin = new Thickness(0, 0, 0, 10)
                    };
                    slider.ValueChanged += (s, ev) =>
                    {
                        if (_suppress) return;
                        _sensValues[key] = ev.NewValue / 100.0;
                        if (!_sensitivityThrottle) { _sensitivityThrottle = true; _ = ThrottledSendSensitivity(); }
                    };
                    EditCategoryContent.Children.Add(slider);
                }
            }

            // Invert toggles
            if (category.InvertKeys.Length > 0)
            {
                AddSectionHeader("Invert");
                EditCategoryContent.Children.Add(new TextBlock
                {
                    Text = "Applies to both sides.",
                    FontSize = 11,
                    Foreground = (Windows.UI.Xaml.Media.SolidColorBrush)Application.Current.Resources["SystemControlForegroundBaseMediumBrush"],
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 4)
                });
                foreach (var key in category.InvertKeys)
                {
                    var label = FormatSettingLabel(key);
                    var toggle = new ToggleSwitch
                    {
                        Header = label, OffContent = "", OnContent = "Inv",
                        IsOn = _invertValues.ContainsKey(key) && _invertValues[key],
                        Tag = key, Margin = new Thickness(0, 0, 0, 4)
                    };
                    toggle.Toggled += (s, ev) =>
                    {
                        if (_suppress) return;
                        _invertValues[key] = ((ToggleSwitch)s).IsOn;
                        _ = SendSensitivityAsync();
                    };
                    EditCategoryContent.Children.Add(toggle);
                }
            }
        }

        async Task ThrottledSendSensitivity()
        {
            try
            {
                await Task.Delay(200);
                await SendSensitivityAsync();
            }
            finally
            {
                _sensitivityThrottle = false;
            }
        }

        void AddSectionHeader(string text)
        {
            EditCategoryContent.Children.Add(new TextBlock
            {
                Text = text,
                FontSize = 13,
                FontWeight = Windows.UI.Text.FontWeights.SemiBold,
                Foreground = (Windows.UI.Xaml.Media.SolidColorBrush)Application.Current.Resources["SystemControlForegroundBaseMediumBrush"],
                Margin = new Thickness(0, 12, 0, 8)
            });
        }

        static string FormatSettingLabel(string key)
        {
            // Axis labels — X/Y mean horizontal/vertical for both sides, not left/right.
            if (key.Equals("stickSensitivityX", StringComparison.OrdinalIgnoreCase))
                return "Horizontal (Left & Right Stick)";
            if (key.Equals("stickSensitivityY", StringComparison.OrdinalIgnoreCase))
                return "Vertical (Left & Right Stick)";
            if (key.Equals("trackpadSensitivityX", StringComparison.OrdinalIgnoreCase))
                return "Horizontal (Left & Right Pad)";
            if (key.Equals("trackpadSensitivityY", StringComparison.OrdinalIgnoreCase))
                return "Vertical (Left & Right Pad)";
            if (key.Equals("gyroSensitivity", StringComparison.OrdinalIgnoreCase))
                return "Gyro ° Sensitivity";
            if (key.Equals("gyroSensitivityX", StringComparison.OrdinalIgnoreCase))
                return "Horizontal (Yaw)";
            if (key.Equals("gyroSensitivityY", StringComparison.OrdinalIgnoreCase))
                return "Vertical (Pitch)";
            if (key.Equals("gyroDotsPer360", StringComparison.OrdinalIgnoreCase))
                return "Dots Per 360°";
            if (key.Equals("invertStickX", StringComparison.OrdinalIgnoreCase))
                return "Horizontal (both sticks)";
            if (key.Equals("invertStickY", StringComparison.OrdinalIgnoreCase))
                return "Vertical (both sticks)";
            if (key.Equals("invertTrackpadX", StringComparison.OrdinalIgnoreCase))
                return "Horizontal (both pads)";
            if (key.Equals("invertTrackpadY", StringComparison.OrdinalIgnoreCase))
                return "Vertical (both pads)";
            if (key.Equals("invertGyroX", StringComparison.OrdinalIgnoreCase))
                return "Horizontal (Yaw)";
            if (key.Equals("invertGyroY", StringComparison.OrdinalIgnoreCase))
                return "Vertical (Pitch)";
            if (key.Equals("stickDeadzone", StringComparison.OrdinalIgnoreCase))
                return "Both sticks";
            if (key.Equals("trackpadDeadzone", StringComparison.OrdinalIgnoreCase))
                return "Both trackpads";
            if (key.Equals("triggerDeadzone", StringComparison.OrdinalIgnoreCase))
                return "Both triggers";
            return key;
        }

        void AddInputCard(string inputId)
        {
            var label = GetInputLabel(inputId);
            var mapped = _inputMap.ContainsKey(inputId) ? _inputMap[inputId] : "None";
            var locked = IsLockedGuideInput(inputId);
            if (locked) mapped = "Guide (locked)";

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var nameBlock = new TextBlock
            {
                Text = label,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 15,
                Foreground = (Windows.UI.Xaml.Media.SolidColorBrush)Application.Current.Resources["SystemControlForegroundBaseHighBrush"]
            };
            Grid.SetColumn(nameBlock, 0);
            grid.Children.Add(nameBlock);

            var mappedBlock = new TextBlock
            {
                Text = mapped,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 14,
                Foreground = (Windows.UI.Xaml.Media.SolidColorBrush)Application.Current.Resources["SystemControlForegroundBaseMediumBrush"],
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Grid.SetColumn(mappedBlock, 1);
            grid.Children.Add(mappedBlock);

            var editHint = new TextBlock
            {
                Text = locked ? "Locked" : "Edit \u203A",
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 13,
                Foreground = (Windows.UI.Xaml.Media.SolidColorBrush)Application.Current.Resources["SystemControlForegroundBaseMediumBrush"],
                Margin = new Thickness(8, 0, 0, 0)
            };
            Grid.SetColumn(editHint, 2);
            grid.Children.Add(editHint);

            // Single button so Gamepad A activates Edit (nested buttons block that).
            var card = new Button
            {
                Content = grid,
                Tag = inputId,
                Style = (Style)Application.Current.Resources["PillButtonStyle"],
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(16, 14, 16, 14),
                Margin = new Thickness(0, 0, 0, 8),
                IsEnabled = !locked
            };
            card.Click += EditCardButton_Click;
            EditCategoryContent.Children.Add(card);
        }

        void EditCardButton_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button btn) || !(btn.Tag is string id)) return;
            OpenRemapView(id);
        }

        // ═══════════════ Remap view event handlers ═══════════════

        string GetCurrentMappedDisplay()
        {
            if (string.IsNullOrEmpty(_remapInputId) || !_inputMap.ContainsKey(_remapInputId))
                return "None";
            var mapped = _inputMap[_remapInputId];
            return string.IsNullOrEmpty(mapped) ? "None" : mapped;
        }

        static readonly string[] RemapTabOrder = { "Gamepad", "Keyboard", "Mouse", "None" };

        void RemapTab_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button btn) || !(btn.Tag is string tab)) return;
            SetRemapTab(tab);
        }

        void CycleRemapTab(int delta)
        {
            if (RemapView.Visibility != Visibility.Visible) return;
            var idx = Array.FindIndex(RemapTabOrder, t =>
                string.Equals(t, _remapTab, StringComparison.OrdinalIgnoreCase));
            if (idx < 0) idx = 0;
            idx = (idx + delta + RemapTabOrder.Length) % RemapTabOrder.Length;
            SetRemapTab(RemapTabOrder[idx]);
        }

        void SetRemapTab(string tab)
        {
            _remapTab = tab;
            if (tab == "Keyboard")
                ApplyStickyModsFromCurrentBinding();
            else
                _stickyMods = 0;

            BuildRemapGrid();
            HighlightRemapTabs();
            BringActiveRemapTabIntoView();
        }

        void BringActiveRemapTabIntoView()
        {
            Button active = null;
            if (string.Equals(_remapTab, "Gamepad", StringComparison.OrdinalIgnoreCase)) active = RemapTabGamepad;
            else if (string.Equals(_remapTab, "Keyboard", StringComparison.OrdinalIgnoreCase)) active = RemapTabKeyboard;
            else if (string.Equals(_remapTab, "Mouse", StringComparison.OrdinalIgnoreCase)) active = RemapTabMouse;
            else if (string.Equals(_remapTab, "None", StringComparison.OrdinalIgnoreCase)) active = RemapTabNone;
            if (active == null || RemapTabsScroller == null || RemapTabsPanel == null) return;

            RemapTabsPanel.UpdateLayout();
            RemapTabsScroller.UpdateLayout();

            var transform = active.TransformToVisual(RemapTabsPanel);
            var bounds = transform.TransformBounds(new Windows.Foundation.Rect(0, 0, active.ActualWidth, active.ActualHeight));
            var viewport = RemapTabsScroller.ViewportWidth;
            if (viewport <= 0 || double.IsNaN(viewport)) return;

            var offset = RemapTabsScroller.HorizontalOffset;
            double? target = null;
            const double pad = 8;
            if (bounds.Left < offset + pad)
                target = Math.Max(0, bounds.Left - pad);
            else if (bounds.Right > offset + viewport - pad)
                target = Math.Max(0, bounds.Right - viewport + pad);

            if (target.HasValue)
                RemapTabsScroller.ChangeView(target.Value, null, null, disableAnimation: false);
        }

        void ApplyStickyModsFromCurrentBinding()
        {
            _stickyMods = 0;
            var mapped = GetCurrentMappedDisplay();
            if (string.Equals(mapped, "None", StringComparison.OrdinalIgnoreCase))
                return;
            if (mapped.StartsWith("Mouse", StringComparison.OrdinalIgnoreCase))
                return;
            if (XboxOutputs.Any(x => !string.Equals(x, "None", StringComparison.OrdinalIgnoreCase)
                && string.Equals(x, mapped, StringComparison.OrdinalIgnoreCase)))
                return;

            var rest = mapped;
            while (true)
            {
                if (rest.StartsWith("Ctrl+", StringComparison.OrdinalIgnoreCase))
                { _stickyMods |= 1; rest = rest.Substring(5); continue; }
                if (rest.StartsWith("Alt+", StringComparison.OrdinalIgnoreCase))
                { _stickyMods |= 2; rest = rest.Substring(4); continue; }
                if (rest.StartsWith("Shift+", StringComparison.OrdinalIgnoreCase))
                { _stickyMods |= 4; rest = rest.Substring(6); continue; }
                if (rest.StartsWith("Win+", StringComparison.OrdinalIgnoreCase))
                { _stickyMods |= 8; rest = rest.Substring(4); continue; }
                break;
            }
        }

        void ResolveRemapTabFromCurrentBinding()
        {
            var mapped = GetCurrentMappedDisplay();
            _stickyMods = 0;

            if (string.Equals(mapped, "None", StringComparison.OrdinalIgnoreCase))
            {
                _remapTab = "Gamepad";
                return;
            }

            if (mapped.StartsWith("Mouse", StringComparison.OrdinalIgnoreCase))
            {
                _remapTab = "Mouse";
                return;
            }

            if (XboxOutputs.Any(x => !string.Equals(x, "None", StringComparison.OrdinalIgnoreCase)
                && string.Equals(x, mapped, StringComparison.OrdinalIgnoreCase)))
            {
                _remapTab = "Gamepad";
                return;
            }

            _remapTab = "Keyboard";
            ApplyStickyModsFromCurrentBinding();
        }

        bool IsCurrentXboxBinding(string xboxId) =>
            string.Equals(GetCurrentMappedDisplay(), xboxId, StringComparison.OrdinalIgnoreCase);

        bool IsCurrentMouseBinding(string mouseId) =>
            string.Equals(GetCurrentMappedDisplay(), "Mouse" + mouseId, StringComparison.OrdinalIgnoreCase);

        bool IsCurrentKeyBinding(int vk)
        {
            var mapped = GetCurrentMappedDisplay();
            var expected = FormatKeyDisplay(vk, _stickyMods);
            if (string.Equals(mapped, expected, StringComparison.OrdinalIgnoreCase))
                return true;

            // Also match when display uses the grid label (e.g. Bksp) instead of canonical name
            foreach (var row in new[] { KeyboardRow1, KeyboardRow2, KeyboardRow3, KeyboardRow4, KeyboardRow5, KeyboardRow6, KeyboardRow7 })
            {
                foreach (var (label, rowVk, _) in row)
                {
                    if (rowVk != vk) continue;
                    var withLabel = FormatKeyDisplayFromName(label, _stickyMods);
                    return string.Equals(mapped, withLabel, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(mapped, FormatKeyDisplay(vk, _stickyMods), StringComparison.OrdinalIgnoreCase);
                }
            }
            return false;
        }

        static string FormatKeyDisplay(int vk, int mods) =>
            FormatKeyDisplayFromName(VkDisplayName(vk), mods);

        static string FormatKeyDisplayFromName(string keyName, int mods)
        {
            var parts = new List<string>();
            if ((mods & 1) != 0) parts.Add("Ctrl");
            if ((mods & 2) != 0) parts.Add("Alt");
            if ((mods & 4) != 0) parts.Add("Shift");
            if ((mods & 8) != 0) parts.Add("Win");
            parts.Add(keyName);
            return string.Join("+", parts);
        }

        static string VkDisplayName(int vk)
        {
            if (vk == 0x08) return "Backspace";
            if (vk == 0x09) return "Tab";
            if (vk == 0x0D) return "Enter";
            if (vk == 0x1B) return "Esc";
            if (vk == 0x20) return "Space";
            if (vk == 0x25) return "Left";
            if (vk == 0x26) return "Up";
            if (vk == 0x27) return "Right";
            if (vk == 0x28) return "Down";
            if (vk == 0x2E) return "Delete";
            if (vk >= 0x30 && vk <= 0x39) return ((char)vk).ToString();
            if (vk >= 0x41 && vk <= 0x5A) return ((char)vk).ToString();
            if (vk >= 0x70 && vk <= 0x7B) return "F" + (vk - 0x6F);
            return "VK_" + vk.ToString("X2", CultureInfo.InvariantCulture);
        }

        void StyleAsCurrentBinding(Button btn, bool isCurrent)
        {
            btn.Style = isCurrent
                ? (Style)Application.Current.Resources["PillAccentButtonStyle"]
                : (Style)Application.Current.Resources["PillButtonStyle"];
        }

        void OpenRemapView(string inputId)
        {
            if (IsLockedGuideInput(inputId)) return;

            _remapInputId = inputId;
            ResolveRemapTabFromCurrentBinding();

            var label = GetInputLabel(inputId);
            var mapped = GetCurrentMappedDisplay();
            RemapTitle.Text = "Select a command for " + label;
            RemapSubtitle.Text = "Currently: " + mapped;

            MainView.Visibility = Visibility.Collapsed;
            RemapView.Visibility = Visibility.Visible;

            BuildRemapGrid();
            HighlightRemapTabs();
        }

        void RemapCancel_Click(object sender, RoutedEventArgs e)
        {
            RemapView.Visibility = Visibility.Collapsed;
            MainView.Visibility = Visibility.Visible;
        }

        void HighlightRemapTabs()
        {
            SetTabActive(RemapTabGamepad, _remapTab == "Gamepad");
            SetTabActive(RemapTabKeyboard, _remapTab == "Keyboard");
            SetTabActive(RemapTabMouse, _remapTab == "Mouse");
            SetTabActive(RemapTabNone, _remapTab == "None");
        }

        static void SetTabActive(Button btn, bool active)
        {
            btn.Style = active
                ? (Style)Application.Current.Resources["PillAccentButtonStyle"]
                : (Style)Application.Current.Resources["PillButtonStyle"];
        }

        void BuildRemapGrid()
        {
            RemapGrid.Children.Clear();

            if (_remapTab == "Gamepad")
                BuildGamepadGrid();
            else if (_remapTab == "Keyboard")
                BuildKeyboardGrid();
            else if (_remapTab == "Mouse")
                BuildMouseGrid();
            else if (_remapTab == "None")
                BuildNoneGrid();
        }

        void BuildNoneGrid()
        {
            RemapGrid.Children.Add(new TextBlock
            {
                Text = "Remove the current binding for this input.",
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12),
                Foreground = (Windows.UI.Xaml.Media.SolidColorBrush)Application.Current.Resources["SystemControlForegroundBaseMediumBrush"]
            });
            var clear = new Button
            {
                Content = "Clear binding",
                Style = (Style)Application.Current.Resources["PillAccentButtonStyle"],
                HorizontalAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(20, 12, 20, 12),
                FontSize = 14
            };
            clear.Click += async (s, e) => await ApplyRemapAsync("none", "", "0");
            RemapGrid.Children.Add(clear);
        }

        void BuildGamepadGrid()
        {
            var groups = new[]
            {
                ("Bumpers & Triggers", new[] { "Lb", "Rb", "Lt", "Rt" }),
                ("Face Buttons", new[] { "A", "B", "X", "Y" }),
                ("DPad", new[] { "DpadUp", "DpadDown", "DpadLeft", "DpadRight" }),
                ("Sticks", new[] { "LeftStick", "RightStick", "LsClick", "RsClick" }),
                ("Menu", new[] { "Back", "Start", "Guide" }),
            };

            foreach (var (groupName, buttons) in groups)
            {
                RemapGrid.Children.Add(new TextBlock
                {
                    Text = groupName,
                    FontSize = 13,
                    FontWeight = Windows.UI.Text.FontWeights.SemiBold,
                    Foreground = (Windows.UI.Xaml.Media.SolidColorBrush)Application.Current.Resources["SystemControlForegroundBaseMediumHighBrush"],
                    Margin = new Thickness(0, 8, 0, 4)
                });

                var wrap = new StackPanel { Orientation = Orientation.Horizontal };
                foreach (var id in buttons)
                {
                    var btn = new Button
                    {
                        Content = id,
                        Tag = "xbox:" + id,
                        Padding = new Thickness(16, 12, 16, 12),
                        Margin = new Thickness(0, 0, 8, 8),
                        MinWidth = 64,
                        FontSize = 14
                    };
                    StyleAsCurrentBinding(btn, IsCurrentXboxBinding(id));
                    btn.Click += RemapGridButton_Click;
                    wrap.Children.Add(btn);
                }
                RemapGrid.Children.Add(wrap);
            }
        }

        void BuildKeyboardGrid()
        {
            var modPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            AddModifierToggle(modPanel, "Ctrl", 1);
            AddModifierToggle(modPanel, "Alt", 2);
            AddModifierToggle(modPanel, "Shift", 4);
            RemapGrid.Children.Add(modPanel);

            // Compact keys + horizontal scroll so rows are not clipped in the Game Bar window.
            var keyboard = new StackPanel();
            var rows = new[]
            {
                KeyboardRow1, KeyboardRow2, KeyboardRow3,
                KeyboardRow4, KeyboardRow5, KeyboardRow6, KeyboardRow7
            };

            foreach (var row in rows)
            {
                var wrap = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 2) };
                foreach (var (label, vk, width) in row)
                {
                    var btn = new Button
                    {
                        Content = label,
                        Tag = "key:" + vk,
                        Padding = new Thickness(4, 8, 4, 8),
                        Margin = new Thickness(0, 0, 2, 2),
                        MinWidth = 26 * width,
                        FontSize = 11,
                        HorizontalContentAlignment = HorizontalAlignment.Center
                    };
                    StyleAsCurrentBinding(btn, IsCurrentKeyBinding(vk));
                    btn.Click += RemapGridButton_Click;
                    wrap.Children.Add(btn);
                }
                keyboard.Children.Add(wrap);
            }

            var scroller = new ScrollViewer
            {
                Content = keyboard,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalScrollMode = ScrollMode.Enabled,
                VerticalScrollMode = ScrollMode.Disabled,
                ZoomMode = ZoomMode.Disabled,
                Padding = new Thickness(0, 0, 0, 4)
            };
            RemapGrid.Children.Add(scroller);
        }

        void AddModifierToggle(StackPanel panel, string label, int flag)
        {
            var toggle = new ToggleButton
            {
                Content = label,
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 6, 0),
                FontSize = 13
            };
            toggle.IsChecked = (_stickyMods & flag) != 0;
            toggle.Checked += (s, _) =>
            {
                _stickyMods |= flag;
                if (_remapTab == "Keyboard")
                    BuildRemapGrid();
            };
            toggle.Unchecked += (s, _) =>
            {
                _stickyMods &= ~flag;
                if (_remapTab == "Keyboard")
                    BuildRemapGrid();
            };
            panel.Children.Add(toggle);
        }

        void BuildMouseGrid()
        {
            var wrap = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            foreach (var output in MouseOutputs)
            {
                var btn = new Button
                {
                    Content = output.Replace("ScrollUp", "Scroll Up").Replace("ScrollDown", "Scroll Down"),
                    Tag = "mouse:" + output,
                    Padding = new Thickness(16, 12, 16, 12),
                    Margin = new Thickness(0, 0, 8, 8),
                    MinWidth = 88,
                    FontSize = 14
                };
                StyleAsCurrentBinding(btn, IsCurrentMouseBinding(output));
                btn.Click += RemapGridButton_Click;
                wrap.Children.Add(btn);
            }
            RemapGrid.Children.Add(wrap);
        }

        async void RemapGridButton_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button btn) || !(btn.Tag is string tag)) return;

            var parts = tag.Split(new[] { ':' }, 2);
            if (parts.Length != 2) return;

            var kind = parts[0];
            var value = parts[1];
            var mods = kind == "key" ? _stickyMods.ToString(CultureInfo.InvariantCulture) : "0";

            await ApplyRemapAsync(kind, value, mods);
        }

        async Task ApplyRemapAsync(string kind, string value, string mods)
        {
            if (string.IsNullOrEmpty(_remapInputId) || string.IsNullOrEmpty(_activeProfileId))
                return;

            if (!await EnsureSharedBindingsEditAllowedAsync())
                return;

            var payload = _activeProfileId + "\t" + _remapInputId + "\t" + kind + "\t" + value + "\t" + mods;
            var resp = await IpcClient.SendAsync("remapAction", payload);
            StatusText.Text = resp.IsOk
                ? (_remapInputId + (kind == "none" ? " cleared" : " remapped to " + value))
                : (resp.Error ?? "Remap failed");

            RemapView.Visibility = Visibility.Collapsed;
            MainView.Visibility = Visibility.Visible;
            await RefreshAsync(force: true);
        }

        // ═══════════════ View panel: controller callouts (read-only) ═══════════════

        void RebuildLayoutLabels()
        {
            LeftLabelsPanel.Children.Clear();
            RightLabelsPanel.Children.Clear();

            if (!_controllerConnected || string.IsNullOrEmpty(_controllerModel))
                return;

            ViewLayoutTitle.Text = string.IsNullOrEmpty(_selectedProfileName) ? "—" : _selectedProfileName;
            ViewLayoutSubtitle.Text = string.IsNullOrEmpty(_activeLayoutName)
                ? "Current layout"
                : _activeLayoutName;

            if (_controllerModel == "sc1")
                BuildSc1Labels();
            else
                BuildSc2Labels();

            // Modes that aren't a single face button — append under the matching side.
            AddModeCallout(LeftLabelsPanel, "left", "L Pad");
            AddModeCallout(RightLabelsPanel, "right", "R Pad");
            AddModeCallout(RightLabelsPanel, "gyro", "Gyro");
        }

        void BuildSc2Labels()
        {
            AddCallout(LeftLabelsPanel, "Lb", "LB");
            AddCallout(LeftLabelsPanel, "Lt", "LT");
            AddCallout(LeftLabelsPanel, "L4", "L4");
            AddCallout(LeftLabelsPanel, "L5", "L5");
            AddCallout(LeftLabelsPanel, "View", "Select");
            AddCallout(LeftLabelsPanel, "LeftStick", "L Stick");
            AddCallout(LeftLabelsPanel, "LsClick", "LS Click");
            AddCallout(LeftLabelsPanel, "DpadUp", "DPad U");
            AddCallout(LeftLabelsPanel, "DpadDown", "DPad D");
            AddCallout(LeftLabelsPanel, "DpadLeft", "DPad L");
            AddCallout(LeftLabelsPanel, "DpadRight", "DPad R");
            AddCallout(LeftLabelsPanel, "LeftTrackpadClick", "L Pad Click");

            AddCallout(RightLabelsPanel, "Rb", "RB");
            AddCallout(RightLabelsPanel, "Rt", "RT");
            AddCallout(RightLabelsPanel, "R4", "R4");
            AddCallout(RightLabelsPanel, "R5", "R5");
            AddCallout(RightLabelsPanel, "Menu", "Start");
            AddCallout(RightLabelsPanel, "Y", "Y");
            AddCallout(RightLabelsPanel, "X", "X");
            AddCallout(RightLabelsPanel, "B", "B");
            AddCallout(RightLabelsPanel, "A", "A");
            AddCallout(RightLabelsPanel, "RightStick", "R Stick");
            AddCallout(RightLabelsPanel, "RsClick", "RS Click");
            AddCallout(RightLabelsPanel, "RightTrackpadClick", "R Pad Click");
            AddCallout(RightLabelsPanel, "Steam", "Steam");
        }

        void BuildSc1Labels()
        {
            AddCallout(LeftLabelsPanel, "Lb", "LB");
            AddCallout(LeftLabelsPanel, "Lt", "LT");
            AddCallout(LeftLabelsPanel, "L4", "L4");
            AddCallout(LeftLabelsPanel, "L5", "L5");
            AddCallout(LeftLabelsPanel, "View", "Select");
            AddCallout(LeftLabelsPanel, "LeftTrackpadClick", "L Pad Click");
            AddCallout(LeftLabelsPanel, "DpadUp", "Pad U");
            AddCallout(LeftLabelsPanel, "DpadDown", "Pad D");
            AddCallout(LeftLabelsPanel, "DpadLeft", "Pad L");
            AddCallout(LeftLabelsPanel, "DpadRight", "Pad R");
            AddCallout(LeftLabelsPanel, "LeftStick", "L Stick");
            AddCallout(LeftLabelsPanel, "LsClick", "LS Click");

            AddCallout(RightLabelsPanel, "Rb", "RB");
            AddCallout(RightLabelsPanel, "Rt", "RT");
            AddCallout(RightLabelsPanel, "R4", "R4");
            AddCallout(RightLabelsPanel, "R5", "R5");
            AddCallout(RightLabelsPanel, "Menu", "Start");
            AddCallout(RightLabelsPanel, "RightTrackpadClick", "R Pad Click");
            AddCallout(RightLabelsPanel, "Y", "Y");
            AddCallout(RightLabelsPanel, "X", "X");
            AddCallout(RightLabelsPanel, "B", "B");
            AddCallout(RightLabelsPanel, "A", "A");
            AddCallout(RightLabelsPanel, "RightStick", "R Stick");
            AddCallout(RightLabelsPanel, "RsClick", "RS Click");
            AddCallout(RightLabelsPanel, "Steam", "Steam");
        }

        void AddModeCallout(StackPanel panel, string modeKey, string label)
        {
            if (!_modeValues.ContainsKey(modeKey)) return;
            var mode = _modeValues[modeKey];
            if (string.IsNullOrEmpty(mode) || mode.Equals("Off", StringComparison.OrdinalIgnoreCase))
                return;
            AddCalloutText(panel, label, FormatModeLabel(mode));
        }

        void AddCallout(StackPanel panel, string inputId, string shortName)
        {
            var mapped = _inputMap.ContainsKey(inputId) ? _inputMap[inputId] : "";
            if (IsLockedGuideInput(inputId)) mapped = "Guide";
            if (string.IsNullOrEmpty(mapped) || mapped.Equals("None", StringComparison.OrdinalIgnoreCase))
                mapped = "—";
            AddCalloutText(panel, shortName, mapped);
        }

        static void AddCalloutText(StackPanel panel, string shortName, string mapped)
        {
            var block = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            block.Children.Add(new TextBlock
            {
                Text = shortName,
                FontSize = 11,
                Opacity = 0.65,
                Foreground = (Windows.UI.Xaml.Media.SolidColorBrush)Application.Current.Resources["SystemControlForegroundBaseMediumBrush"]
            });
            block.Children.Add(new TextBlock
            {
                Text = mapped,
                FontSize = 13,
                FontWeight = Windows.UI.Text.FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Windows.UI.Xaml.Media.SolidColorBrush)Application.Current.Resources["SystemControlForegroundBaseHighBrush"]
            });
            panel.Children.Add(block);
        }

        string GetInputLabel(string inputId)
        {
            var spot = _layout.FirstOrDefault(s =>
                string.Equals(s.GetNamedString("inputId"), inputId, StringComparison.OrdinalIgnoreCase));
            if (spot != null)
                return spot.GetNamedString("label", inputId);

            // Friendly fallbacks when layout metadata is missing
            if (inputId.Equals("L4", StringComparison.OrdinalIgnoreCase)) return "L4 (Upper Left Grip)";
            if (inputId.Equals("L5", StringComparison.OrdinalIgnoreCase)) return "L5 (Lower Left Grip)";
            if (inputId.Equals("R4", StringComparison.OrdinalIgnoreCase)) return "R4 (Upper Right Grip)";
            if (inputId.Equals("R5", StringComparison.OrdinalIgnoreCase)) return "R5 (Lower Right Grip)";
            if (inputId.Equals("View", StringComparison.OrdinalIgnoreCase)) return "Select";
            if (inputId.Equals("Menu", StringComparison.OrdinalIgnoreCase)) return "Start";
            if (inputId.Equals("LeftTrackpadClick", StringComparison.OrdinalIgnoreCase)) return "L Pad Click";
            if (inputId.Equals("RightTrackpadClick", StringComparison.OrdinalIgnoreCase)) return "R Pad Click";
            return inputId;
        }

        // ═══════════════ Refresh / Bind ═══════════════

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
                    if (!_hadState || _misses >= 4)
                    {
                        StatusText.Text = "Host offline \u2014 start MistMapper.exe";
                        ShowAlert("Host not running. Start the tray app, then refresh.");
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

        void ShowAlert(string message)
        {
            DepBanner.Visibility = Visibility.Visible;
            DepBannerText.Text = message;
        }

        void HideAlert()
        {
            DepBanner.Visibility = Visibility.Collapsed;
        }

        async Task LoadGameIconAsync()
        {
            try
            {
                var file = await ApplicationData.Current.LocalFolder.GetFileAsync("current-game-icon.png");
                using (var stream = await file.OpenReadAsync())
                {
                    var bmp = new BitmapImage();
                    await bmp.SetSourceAsync(stream);
                    GameIconImage.Source = bmp;
                }
            }
            catch
            {
                GameIconBorder.Visibility = Visibility.Collapsed;
                GameIconImage.Source = null;
            }
        }

        void ParseControllers(JsonObject state)
        {
            _controllers.Clear();
            if (!state.ContainsKey("controllers")) return;
            var arr = state.GetNamedArray("controllers");
            for (var i = 0; i < arr.Count; i++)
            {
                var item = arr.GetObjectAt((uint)i);
                _controllers.Add(new ControllerPadInfo
                {
                    DeviceKey = item.GetNamedString("deviceKey", ""),
                    Model = item.GetNamedString("model", ""),
                    DisplayName = item.GetNamedString("displayName", ""),
                    ProfileId = item.GetNamedString("profileId", ""),
                    Order = (int)item.GetNamedNumber("order", i),
                    Connected = item.GetNamedBoolean("connected", true),
                    HasProfileOverride = item.GetNamedBoolean("hasProfileOverride", false)
                });
            }

            if (string.IsNullOrEmpty(_selectedDeviceKey) && _controllers.Count > 0)
                _selectedDeviceKey = _controllers.OrderBy(c => c.Order).First().DeviceKey;
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
                var gameName = state.GetNamedString("currentGameName", "");
                if (string.IsNullOrWhiteSpace(gameName))
                    gameName = game;
                var source = state.GetNamedString("activeProfileSource", "");
                var depError = state.GetNamedBoolean("dependencyError", false);
                var viiperOk = state.GetNamedBoolean("viiperOk", false);
                var viiperDetail = state.GetNamedString("viiperDetail", "");
                var padOk = state.GetNamedBoolean("controllerConnected", false);
                var model = state.GetNamedString("controllerModel", "");
                _selectedDeviceKey = state.GetNamedString("selectedDeviceKey", "");
                _selectedHasProfileOverride = state.GetNamedBoolean("selectedHasProfileOverride", false);
                ParseControllers(state);
                RebuildControllerStrips(force);

                // Prefer selected pad model for outline when available.
                var selectedPad = _controllers.FirstOrDefault(c =>
                    string.Equals(c.DeviceKey, _selectedDeviceKey, StringComparison.OrdinalIgnoreCase));
                if (selectedPad != null && !string.IsNullOrEmpty(selectedPad.Model))
                    model = selectedPad.Model;

                var connectedChanged = padOk != _controllerConnected;
                var modelChanged = !string.Equals(_controllerModel, model, StringComparison.OrdinalIgnoreCase);
                _controllerConnected = padOk;
                _controllerModel = model ?? "";
                if (connectedChanged || modelChanged)
                {
                    ApplyControllerOutline();
                    if (_controllerConnected)
                        RebuildLayoutLabels();
                }

                BridgeToggle.IsOn = bridgeOn;
                PauseSteamToggle.IsOn = pauseSteam;
                StatusText.Text = status;
                if (string.IsNullOrEmpty(game))
                {
                    GameText.Text = "No game \u00b7 " + activeName;
                }
                else
                {
                    var forGame = source.Equals("GameRule", StringComparison.OrdinalIgnoreCase);
                    GameText.Text = forGame
                        ? "Playing " + gameName + " \u00b7 layout " + activeName
                        : "Playing " + gameName + " \u00b7 " + activeName + " (not bound yet \u2014 edit to create)";
                }

                var hasIcon = state.GetNamedBoolean("hasGameIcon", false);
                var iconToken = state.GetNamedString("gameIconToken", "");
                if (hasIcon && !string.IsNullOrEmpty(iconToken))
                {
                    GameIconBorder.Visibility = Visibility.Visible;
                    if (!string.Equals(iconToken, _lastGameIconToken, StringComparison.Ordinal))
                    {
                        _lastGameIconToken = iconToken;
                        _ = LoadGameIconAsync();
                    }
                }
                else
                {
                    GameIconBorder.Visibility = Visibility.Collapsed;
                    GameIconImage.Source = null;
                    _lastGameIconToken = "";
                }

                if (depError || !viiperOk)
                {
                    var msg = !viiperOk
                        ? (string.IsNullOrEmpty(viiperDetail) ? "VIIPER is required but not available." : viiperDetail)
                        : "";
                    if (depError && viiperOk)
                        msg = "Dependency error. Check host logs.";
                    ShowAlert(msg);
                }
                else if (!padOk)
                {
                    ShowAlert("Controller not connected.");
                }
                else
                {
                    HideAlert();
                }

                var gameBarOverride = state.GetNamedBoolean("gameBarOverrideActive", false);
                OverrideBanner.Visibility = gameBarOverride ? Visibility.Visible : Visibility.Collapsed;

                // User layouts + official template catalog
                if (state.ContainsKey("profiles"))
                {
                    _profileNames.Clear();
                    foreach (var item in state.GetNamedArray("profiles"))
                        _profileNames.Add(item.GetObject().GetNamedString("name"));
                }
                _selectedProfileName = activeName ?? "";
                _activeLayoutId = state.GetNamedString("activeLayoutId", "");
                _activeLayoutName = state.GetNamedString("activeLayoutName", "Custom");
                CurrentLayoutTitle.Text = string.IsNullOrEmpty(_selectedProfileName) ? "—" : _selectedProfileName;
                CurrentLayoutSubtitle.Text = string.IsNullOrEmpty(_activeLayoutName)
                    ? "Custom mappings"
                    : "Based on " + _activeLayoutName;

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
                    _layoutsReady = _officialLayouts.Count > 0;
                }

                _modeValues["left"] = state.GetNamedString("leftTrackpad", "Off");
                _modeValues["right"] = state.GetNamedString("rightTrackpad", "Off");
                _modeValues["gyro"] = state.GetNamedString("gyro", "Off");

                foreach (var key in new[] { "stickSensitivityX", "stickSensitivityY", "trackpadSensitivityX",
                    "trackpadSensitivityY", "gyroSensitivity", "gyroSensitivityX", "gyroSensitivityY", "gyroDotsPer360",
                    "stickDeadzone", "trackpadDeadzone", "triggerDeadzone" })
                    _sensValues[key] = state.GetNamedNumber(key, _sensValues.ContainsKey(key) ? _sensValues[key] : 1.0);

                foreach (var key in new[] { "invertStickX", "invertStickY", "invertTrackpadX",
                    "invertTrackpadY", "invertGyroX", "invertGyroY" })
                    _invertValues[key] = state.GetNamedBoolean(key, false);

                _gyroButtonMode = state.GetNamedString("gyroButtonMode", "HoldToEnable");
                _gyroButtonCombine = state.GetNamedString("gyroButtonCombine", "Any");
                _gyroButtons.Clear();
                if (state.ContainsKey("gyroButtons"))
                {
                    foreach (var item in state.GetNamedArray("gyroButtons"))
                        _gyroButtons.Add(item.GetString());
                }
                ReadPadSettings(state, "leftTrackpadSettings", "left");
                ReadPadSettings(state, "rightTrackpadSettings", "right");

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
                    if (!string.Equals(key, _lastLayoutKey, StringComparison.Ordinal) || force || _layout.Count == 0)
                    {
                        _layout.Clear();
                        _remappable.Clear();
                        foreach (var item in arr)
                        {
                            var obj = item.GetObject();
                            _layout.Add(obj);
                            var id = obj.GetNamedString("inputId", "");
                            _remappable[id] = obj.GetNamedBoolean("remappable", true) && !IsLockedGuideInput(id);
                        }
                        _lastLayoutKey = key;
                        layoutChanged = true;
                    }
                }

                // Only rebuild heavy UI when mappings actually change. Rebuilding every poll
                // tears down ComboBoxes and closes open dropdowns / resets sliders.
                var mapKey = string.Join("|",
                    _inputMap.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                        .Select(kv => kv.Key + "=" + kv.Value));
                var mapChanged = !string.Equals(mapKey, _lastInputMapKey, StringComparison.Ordinal);
                if (force || layoutChanged || mapChanged)
                {
                    _lastInputMapKey = mapKey;
                    RebuildLayoutLabels();
                    if (_activeViewTab == "Edit")
                        RebuildEditCategoryContent();
                }
            }
            finally
            {
                _suppress = false;
            }
        }

        static bool IsLockedGuideInput(string id) =>
            id.Equals("Steam", StringComparison.OrdinalIgnoreCase) ||
            id.Equals("Guide", StringComparison.OrdinalIgnoreCase);
    }
}
