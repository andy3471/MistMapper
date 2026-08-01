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
        static readonly string[] MouseHapticsIntensities = { "Off", "Low", "Medium", "High" };
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

        IEnumerable<(string Id, string Label)> GyroActivationChoicesForModel()
        {
            foreach (var choice in GyroActivationChoices)
            {
                if (_controllerModel == "sc1" && Sc1AbsentInputs.Contains(choice.Id))
                    continue;
                if (_controllerModel == "sc1" && choice.Id is "R4" or "L4")
                {
                    yield return (choice.Id, choice.Id == "L4" ? "Left Grip" : "Right Grip");
                    continue;
                }
                if (_controllerModel is "dualsense" or "dualsense-edge")
                {
                    if (DualSenseAbsentInputs.Contains(choice.Id)) continue;
                    if (_controllerModel != "dualsense-edge" && choice.Id is "L4" or "R4") continue;
                }
                yield return choice;
            }
        }

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
        string _remapActivator = "Regular";
        int _remapSlot;
        /// <summary>False = command list; true = old Gamepad/Keyboard/Mouse picker for one slot.</summary>
        bool _remapPickMode;
        readonly Dictionary<string, List<(string Activator, List<string> Actions)>> _bindingsByInput =
            new Dictionary<string, List<(string, List<string>)>>(StringComparer.OrdinalIgnoreCase);
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
            public bool RumbleEnabled = true;
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
            ["left"] = false, ["right"] = false
        };
        readonly Dictionary<string, string> _trackballFriction = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["left"] = "High", ["right"] = "High"
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
        readonly Dictionary<string, string> _mouseHaptics = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["left"] = "Medium", ["right"] = "Medium"
        };
        readonly Dictionary<string, double> _flickSensitivity = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["left"] = 1, ["right"] = 1
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
            || ListPickerView.Visibility == Visibility.Visible
            || TextPromptView.Visibility == Visibility.Visible;

        TaskCompletionSource<bool> _advancedSettingsTcs;
        TaskCompletionSource<int> _listPickerTcs;
        TaskCompletionSource<string> _textPromptTcs;

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

            var path = _controllerModel switch
            {
                "sc1" => "ms-appx:///Assets/controller-outline-sc1.png",
                "dualsense" or "dualsense-edge" => "ms-appx:///Assets/controller-outline-dualsense.png",
                _ => "ms-appx:///Assets/controller-outline-sc2.png"
            };
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
        void WidgetPage_PreviewKeyDown(object sender, Windows.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            var key = e.Key;

            if (TextPromptView.Visibility == Visibility.Visible)
            {
                if (key == Windows.System.VirtualKey.GamepadB || key == Windows.System.VirtualKey.Escape)
                {
                    TextPromptCancel_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                }
                else if (key == Windows.System.VirtualKey.GamepadA
                         || key == Windows.System.VirtualKey.Enter)
                {
                    TextPromptSave_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                }
                return;
            }

            // Don't steal keys while typing in a text box
            if (FocusManager.GetFocusedElement() is TextBox)
                return;

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
                if (_remapPickMode && key == Windows.System.VirtualKey.GamepadX)
                { CycleRemapTab(-1); e.Handled = true; }
                else if (_remapPickMode && key == Windows.System.VirtualKey.GamepadB)
                { CycleRemapTab(1); e.Handled = true; }
                else if (!_remapPickMode && (key == Windows.System.VirtualKey.GamepadB
                    || key == Windows.System.VirtualKey.Escape))
                {
                    RemapCancel_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                }
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
                && PreviewLayoutView.Visibility != Visibility.Visible
                && TextPromptView.Visibility != Visibility.Visible)
                MainView.Visibility = Visibility.Visible;
        }

        void CloseListPicker()
        {
            ListPickerView.Visibility = Visibility.Collapsed;
        }

        /// <summary>In-widget text prompt (ContentDialog chrome looks wrong in Game Bar).</summary>
        async Task<string> PromptTextAsync(string title, string header, string initial)
        {
            _textPromptTcs?.TrySetResult(null);
            _textPromptTcs = new TaskCompletionSource<string>();

            TextPromptTitle.Text = title ?? "Name";
            TextPromptHeader.Text = header ?? "";
            TextPromptHeader.Visibility = string.IsNullOrWhiteSpace(header)
                ? Visibility.Collapsed
                : Visibility.Visible;
            TextPromptBox.Text = initial ?? "";

            MainView.Visibility = Visibility.Collapsed;
            TextPromptView.Visibility = Visibility.Visible;

            // Focus after layout so the caret is ready for keyboard / Game Bar.
            _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                TextPromptBox.Focus(FocusState.Programmatic);
                TextPromptBox.SelectAll();
            });

            return await _textPromptTcs.Task;
        }

        void TextPromptBox_KeyDown(object sender, Windows.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                TextPromptSave_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Escape)
            {
                TextPromptCancel_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
        }

        void TextPromptSave_Click(object sender, RoutedEventArgs e)
        {
            var text = TextPromptBox.Text ?? "";
            _textPromptTcs?.TrySetResult(text);
            CloseTextPrompt();
            RestoreViewAfterTextPrompt();
        }

        void TextPromptCancel_Click(object sender, RoutedEventArgs e)
        {
            _textPromptTcs?.TrySetResult(null);
            CloseTextPrompt();
            RestoreViewAfterTextPrompt();
        }

        void CloseTextPrompt()
        {
            TextPromptView.Visibility = Visibility.Collapsed;
        }

        void RestoreViewAfterTextPrompt()
        {
            if (AdvancedSettingsView.Visibility == Visibility.Visible
                || SettingsView.Visibility == Visibility.Visible
                || RemapView.Visibility == Visibility.Visible
                || BrowseLayoutView.Visibility == Visibility.Visible
                || PreviewLayoutView.Visibility == Visibility.Visible
                || ListPickerView.Visibility == Visibility.Visible)
                return;
            MainView.Visibility = Visibility.Visible;
        }
        // ═══════════════ Edit panel: category tabs + content ═══════════════

        void RebuildEditCategoryTabs()
        {
            EditCategoryTabs.Children.Clear();
            foreach (var cat in EditCategories)
            {
                var btn = new Button
                {
                    Content = CategoryDisplayName(cat.Name),
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

        string CategoryDisplayName(string categoryName)
        {
            if (categoryName == "Trackpads" && _controllerModel is "dualsense" or "dualsense-edge")
                return "Touchpad";
            return categoryName;
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

        static readonly HashSet<string> Sc1AbsentInputs = new(StringComparer.OrdinalIgnoreCase)
        {
            "RightStick", "RsClick", "RightStickTouch", "LeftStickTouch", "L5", "R5"
        };

        static readonly HashSet<string> DualSenseAbsentInputs = new(StringComparer.OrdinalIgnoreCase)
        {
            "LeftTrackpad", "LeftTrackpadClick", "L5", "R5", "LeftStickTouch", "RightStickTouch"
        };

        IEnumerable<string> FilterInputsForModel(IEnumerable<string> inputIds)
        {
            if (_controllerModel == "sc1")
                return inputIds.Where(id => !Sc1AbsentInputs.Contains(id));
            if (_controllerModel is "dualsense" or "dualsense-edge")
            {
                var absent = DualSenseAbsentInputs;
                if (_controllerModel != "dualsense-edge")
                    return inputIds.Where(id => !absent.Contains(id) && id is not ("L4" or "R4"));
                return inputIds.Where(id => !absent.Contains(id));
            }
            return inputIds;
        }

        IEnumerable<string> FilterModeKeysForModel(IEnumerable<string> modeKeys)
        {
            // DualSense has one capacitive surface (mapped as RightTrackpad), not two Steam pads.
            if (_controllerModel is "dualsense" or "dualsense-edge")
                return modeKeys.Where(k => k is not "left");
            return modeKeys;
        }

        string ModeSurfaceLabel(string modeKey) => modeKey switch
        {
            "gyro" => "Gyro",
            "left" => "Left pad",
            "right" when _controllerModel is "dualsense" or "dualsense-edge" => "Touchpad",
            "right" => "Right pad",
            _ => modeKey
        };

        void RebuildEditCategoryContentCore()
        {
            EditCategoryContent.Children.Clear();
            var category = EditCategories.FirstOrDefault(c => c.Name == _activeEditCategory);
            if (category.Name == null) return;

            // Input cards
            if (category.InputIds.Length > 0)
            {
                foreach (var inputId in FilterInputsForModel(category.InputIds))
                    AddInputCard(inputId);
            }

            // Mode pickers (trackpad/gyro) — behavior dropdown + Steam-style cog for advanced settings
            if (category.ModeKeys.Length > 0)
            {
                AddSectionHeader("Behavior");
                foreach (var modeKey in FilterModeKeysForModel(category.ModeKeys))
                {
                    var label = ModeSurfaceLabel(modeKey);
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

                    var showCog = category.Name != "Trackpads"
                        || TrackpadModeHasAdvancedSettings(current);
                    if (showCog)
                    {
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
                    }
                    else
                    {
                        btn.Margin = new Thickness(0, 0, 0, 0);
                    }

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

            // Deadzone sliders — only modes that use absolute pad position.
            // (As Mouse / Mouse Joystick ignore TrackpadDeadzone.)
            if (category.DeadzoneKeys.Length > 0)
            {
                var showDeadzone = true;
                if (category.Name == "Trackpads")
                {
                    var left = _modeValues.ContainsKey("left") ? _modeValues["left"] : "Off";
                    var right = _modeValues.ContainsKey("right") ? _modeValues["right"] : "Off";
                    showDeadzone = TrackpadModeUsesDeadzone(left) || TrackpadModeUsesDeadzone(right);
                }

                if (showDeadzone)
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

        static void SetTabActive(Button btn, bool active)
        {
            btn.Style = active
                ? (Style)Application.Current.Resources["PillAccentButtonStyle"]
                : (Style)Application.Current.Resources["PillButtonStyle"];
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
            else if (_controllerModel is "dualsense" or "dualsense-edge")
                BuildDualSenseLabels();
            else
                BuildSc2Labels();

            // Modes that aren't a single face button — append under the matching side.
            if (_controllerModel is "dualsense" or "dualsense-edge")
            {
                AddModeCallout(RightLabelsPanel, "right", "Touchpad");
                AddModeCallout(RightLabelsPanel, "gyro", "Gyro");
            }
            else
            {
                AddModeCallout(LeftLabelsPanel, "left", "L Pad");
                AddModeCallout(RightLabelsPanel, "right", "R Pad");
                AddModeCallout(RightLabelsPanel, "gyro", "Gyro");
            }
        }

        void BuildDualSenseLabels()
        {
            AddCallout(LeftLabelsPanel, "Lb", "L1");
            AddCallout(LeftLabelsPanel, "Lt", "L2");
            AddCallout(LeftLabelsPanel, "View", "Create");
            AddCallout(LeftLabelsPanel, "LeftStick", "L Stick");
            AddCallout(LeftLabelsPanel, "LsClick", "L3");
            AddCallout(LeftLabelsPanel, "DpadUp", "DPad U");
            AddCallout(LeftLabelsPanel, "DpadDown", "DPad D");
            AddCallout(LeftLabelsPanel, "DpadLeft", "DPad L");
            AddCallout(LeftLabelsPanel, "DpadRight", "DPad R");
            if (_controllerModel == "dualsense-edge")
                AddCallout(LeftLabelsPanel, "L4", "L Paddle");

            AddCallout(RightLabelsPanel, "Rb", "R1");
            AddCallout(RightLabelsPanel, "Rt", "R2");
            AddCallout(RightLabelsPanel, "Menu", "Options");
            AddCallout(RightLabelsPanel, "Y", "△");
            AddCallout(RightLabelsPanel, "X", "□");
            AddCallout(RightLabelsPanel, "B", "○");
            AddCallout(RightLabelsPanel, "A", "✕");
            AddCallout(RightLabelsPanel, "RightStick", "R Stick");
            AddCallout(RightLabelsPanel, "RsClick", "R3");
            AddCallout(RightLabelsPanel, "RightTrackpadClick", "Touch Click");
            AddCallout(RightLabelsPanel, "Steam", "PS");
            if (_controllerModel == "dualsense-edge")
                AddCallout(RightLabelsPanel, "R4", "R Paddle");
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
            AddCallout(LeftLabelsPanel, "L4", "L Grip");
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
            AddCallout(RightLabelsPanel, "R4", "R Grip");
            AddCallout(RightLabelsPanel, "Menu", "Start");
            AddCallout(RightLabelsPanel, "RightTrackpadClick", "R Pad Click");
            AddCallout(RightLabelsPanel, "Y", "Y");
            AddCallout(RightLabelsPanel, "X", "X");
            AddCallout(RightLabelsPanel, "B", "B");
            AddCallout(RightLabelsPanel, "A", "A");
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
            if (inputId.Equals("L4", StringComparison.OrdinalIgnoreCase))
                return _controllerModel == "sc1" ? "Left Grip" : "L4 (Upper Left Grip)";
            if (inputId.Equals("L5", StringComparison.OrdinalIgnoreCase)) return "L5 (Lower Left Grip)";
            if (inputId.Equals("R4", StringComparison.OrdinalIgnoreCase))
                return _controllerModel == "sc1" ? "Right Grip" : "R4 (Upper Right Grip)";
            if (inputId.Equals("R5", StringComparison.OrdinalIgnoreCase)) return "R5 (Lower Right Grip)";
            if (inputId.Equals("View", StringComparison.OrdinalIgnoreCase)) return "Select";
            if (inputId.Equals("Menu", StringComparison.OrdinalIgnoreCase)) return "Start";
            if (inputId.Equals("LeftTrackpadClick", StringComparison.OrdinalIgnoreCase)) return "L Pad Click";
            if (inputId.Equals("RightTrackpadClick", StringComparison.OrdinalIgnoreCase))
                return _controllerModel is "dualsense" or "dualsense-edge" ? "Touchpad Click" : "R Pad Click";
            return inputId;
        }
    }
}
