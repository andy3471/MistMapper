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

        static string FormatModeLabel(string mode) => mode switch
        {
            "Off" => "Off",
            "AsMouse" => "Mouse",
            "AsMouseJoystick" => "Mouse Joystick",
            "AsLeftStick" => "Left Stick",
            "AsRightStick" => "Right Stick",
            "AsDpad" => "D-Pad",
            "FlickStick" => "Flick Stick",
            "ScrollWheel" => "Scroll Wheel",
            "ButtonPad" => "Button Pad",
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
                new[] { "gyroSensitivityX", "gyroSensitivityY" }, Array.Empty<string>(),
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

        // Sensitivity/deadzone/invert state from host
        readonly Dictionary<string, double> _sensValues = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["stickSensitivityX"] = 1, ["stickSensitivityY"] = 1,
            ["trackpadSensitivityX"] = 1, ["trackpadSensitivityY"] = 1,
            ["gyroSensitivityX"] = 1, ["gyroSensitivityY"] = 1,
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
            || SettingsView.Visibility == Visibility.Visible;

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
            MainView.Visibility = Visibility.Collapsed;
            SettingsView.Visibility = Visibility.Visible;
        }

        void SettingsBack_Click(object sender, RoutedEventArgs e)
        {
            SettingsView.Visibility = Visibility.Collapsed;
            MainView.Visibility = Visibility.Visible;
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
            if (selected < 0) return;

            var mode = modes[selected];
            if (string.Equals(mode, current, StringComparison.OrdinalIgnoreCase))
                return;

            _modeValues[tag] = mode;
            if (btn.Content is StackPanel sp && sp.Children.Count > 1 && sp.Children[1] is TextBlock valueLabel)
                valueLabel.Text = FormatModeLabel(mode);

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

        /// <summary>Gamepad-friendly list picker (ComboBox flyouts break in Game Bar).</summary>
        static async Task<int> PickListIndexAsync(string title, IReadOnlyList<string> items, string selectedValue)
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

        static async Task<int> PickListIndexAsync(string title, IReadOnlyList<string> items, int selectedIndex)
        {
            if (items == null || items.Count == 0) return -1;

            var list = new ListView
            {
                SelectionMode = ListViewSelectionMode.Single,
                IsItemClickEnabled = true,
                MaxHeight = 320,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            foreach (var item in items)
                list.Items.Add(item);
            if (selectedIndex >= 0 && selectedIndex < items.Count)
                list.SelectedIndex = selectedIndex;
            else
                list.SelectedIndex = 0;

            var accepted = false;
            var picked = -1;
            ContentDialog dialog = null;

            void AcceptCurrent()
            {
                if (list.SelectedIndex < 0) return;
                picked = list.SelectedIndex;
                accepted = true;
                dialog?.Hide();
            }

            // A / click on a row should select immediately (not require the Primary button).
            list.ItemClick += (s, args) =>
            {
                list.SelectedItem = args.ClickedItem;
                AcceptCurrent();
            };
            list.KeyDown += (s, e) =>
            {
                if (e.Key == Windows.System.VirtualKey.GamepadA
                    || e.Key == Windows.System.VirtualKey.Enter
                    || e.Key == Windows.System.VirtualKey.Space)
                {
                    AcceptCurrent();
                    e.Handled = true;
                }
            };

            dialog = new ContentDialog
            {
                Title = title,
                Content = list,
                PrimaryButtonText = "Select",
                CloseButtonText = "Cancel",
                // None so Gamepad A activates the focused list item, not the Primary button.
                DefaultButton = ContentDialogButton.None
            };

            dialog.Opened += (s, e) =>
            {
                list.Focus(FocusState.Programmatic);
                if (list.SelectedItem != null)
                    list.ScrollIntoView(list.SelectedItem);
            };

            var result = await dialog.ShowAsync();
            if (accepted) return picked;
            if (result == ContentDialogResult.Primary) return list.SelectedIndex;
            return -1;
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
            var parts = new List<string> { "\"profileId\":\"" + _activeProfileId + "\"" };
            foreach (var kv in _sensValues)
                parts.Add("\"" + kv.Key + "\":" + kv.Value.ToString(CultureInfo.InvariantCulture));
            foreach (var kv in _invertValues)
                parts.Add("\"" + kv.Key + "\":" + (kv.Value ? "true" : "false"));
            var json = "{" + string.Join(",", parts) + "}";
            var resp = await IpcClient.SendAsync("setSensitivity", json);
            if (!resp.IsOk)
                StatusText.Text = resp.Error ?? "Sensitivity update failed";
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

            // Mode pickers (trackpad/gyro) — buttons open a ContentDialog list
            if (category.ModeKeys.Length > 0)
            {
                AddSectionHeader("Mode");
                foreach (var modeKey in category.ModeKeys)
                {
                    var label = modeKey == "gyro" ? "Gyro" : (modeKey == "left" ? "Left pad" : "Right pad");
                    var current = _modeValues.ContainsKey(modeKey) ? _modeValues[modeKey] : "Off";
                    var btn = new Button
                    {
                        Tag = modeKey,
                        Style = (Style)Application.Current.Resources["PillButtonStyle"],
                        Margin = new Thickness(0, 0, 0, 8),
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        HorizontalContentAlignment = HorizontalAlignment.Left,
                        Padding = new Thickness(16, 12, 16, 12)
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
                    EditCategoryContent.Children.Add(btn);
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
            if (key.Equals("gyroSensitivityX", StringComparison.OrdinalIgnoreCase))
                return "Horizontal (Yaw)";
            if (key.Equals("gyroSensitivityY", StringComparison.OrdinalIgnoreCase))
                return "Vertical (Pitch)";
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
                    "trackpadSensitivityY", "gyroSensitivityX", "gyroSensitivityY",
                    "stickDeadzone", "trackpadDeadzone", "triggerDeadzone" })
                    _sensValues[key] = state.GetNamedNumber(key, _sensValues.ContainsKey(key) ? _sensValues[key] : 1.0);

                foreach (var key in new[] { "invertStickX", "invertStickY", "invertTrackpadX",
                    "invertTrackpadY", "invertGyroX", "invertGyroY" })
                    _invertValues[key] = state.GetNamedBoolean(key, false);

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
