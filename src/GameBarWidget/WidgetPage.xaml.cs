using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Gaming.XboxGameBar;
using Windows.Data.Json;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
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

        // View panel layout categories
        static readonly (string Category, string[] InputIds)[] LayoutCategories =
        {
            ("Left Side", new[] { "Lb", "Lt", "View", "L4", "L5" }),
            ("Right Side", new[] { "Rb", "Rt", "Menu", "R4", "R5" }),
            ("Face Buttons", new[] { "A", "B", "X", "Y" }),
            ("DPad", new[] { "DpadUp", "DpadDown", "DpadLeft", "DpadRight" }),
            ("Left Joystick", new[] { "LeftStick", "LsClick" }),
            ("Right Joystick", new[] { "RightStick", "RsClick" }),
        };

        readonly DispatcherTimer _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        XboxGameBarWidget _widget;
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
            RebuildEditCategoryTabs();
        }

        bool IsOverlayOpen() =>
            RemapView.Visibility == Visibility.Visible
            || BrowseLayoutView.Visibility == Visibility.Visible
            || PreviewLayoutView.Visibility == Visibility.Visible;

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
                CategorySummaries.Visibility = Visibility.Collapsed;
                return;
            }

            NoControllerBanner.Visibility = Visibility.Collapsed;
            ControllerLayoutGrid.Visibility = Visibility.Visible;
            CategorySummaries.Visibility = Visibility.Visible;

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

        async void BindGame_Click(object sender, RoutedEventArgs e)
        {
            var resp = await IpcClient.SendAsync("bindToCurrentGame", "");
            StatusText.Text = resp.IsOk ? "Bound layout to current game." : (resp.Error ?? "Bind failed");
            await RefreshAsync(force: true);
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
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        HorizontalContentAlignment = HorizontalAlignment.Left,
                        Padding = new Thickness(12, 10, 12, 10),
                        Margin = new Thickness(0, 0, 0, 6)
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
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        HorizontalContentAlignment = HorizontalAlignment.Left,
                        Padding = new Thickness(12, 10, 12, 10),
                        Margin = new Thickness(0, 0, 0, 6)
                    };
                    if (string.Equals(name, _selectedProfileName, StringComparison.OrdinalIgnoreCase))
                        btn.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
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
            PreviewLeftLabels.Children.Clear();
            PreviewRightLabels.Children.Clear();
            PreviewCategorySummaries.Children.Clear();

            void Add(StackPanel panel, string id, string display)
            {
                var mapped = map.ContainsKey(id) ? map[id] : "None";
                var text = string.IsNullOrEmpty(mapped) || mapped == "None"
                    ? display
                    : display + " \u2192 " + mapped;
                panel.Children.Add(new TextBlock
                {
                    Text = text,
                    FontSize = 11,
                    Margin = new Thickness(0, 0, 0, 2),
                    TextWrapping = TextWrapping.Wrap
                });
            }

            if (_controllerModel == "sc1")
            {
                Add(PreviewLeftLabels, "Lb", "LB");
                Add(PreviewLeftLabels, "Lt", "LT");
                Add(PreviewLeftLabels, "L4", "L4");
                Add(PreviewLeftLabels, "L5", "L5");
                Add(PreviewLeftLabels, "View", "Select");
                Add(PreviewRightLabels, "Rb", "RB");
                Add(PreviewRightLabels, "Rt", "RT");
                Add(PreviewRightLabels, "R4", "R4");
                Add(PreviewRightLabels, "R5", "R5");
                Add(PreviewRightLabels, "Menu", "Start");
                Add(PreviewRightLabels, "A", "A");
                Add(PreviewRightLabels, "B", "B");
                Add(PreviewRightLabels, "X", "X");
                Add(PreviewRightLabels, "Y", "Y");
            }
            else
            {
                Add(PreviewLeftLabels, "Lb", "LB");
                Add(PreviewLeftLabels, "Lt", "LT");
                Add(PreviewLeftLabels, "L4", "L4");
                Add(PreviewLeftLabels, "L5", "L5");
                Add(PreviewLeftLabels, "View", "Select");
                Add(PreviewRightLabels, "Rb", "RB");
                Add(PreviewRightLabels, "Rt", "RT");
                Add(PreviewRightLabels, "R4", "R4");
                Add(PreviewRightLabels, "R5", "R5");
                Add(PreviewRightLabels, "Menu", "Start");
                Add(PreviewRightLabels, "A", "A");
                Add(PreviewRightLabels, "B", "B");
                Add(PreviewRightLabels, "X", "X");
                Add(PreviewRightLabels, "Y", "Y");
            }

            foreach (var (category, inputIds) in LayoutCategories)
            {
                PreviewCategorySummaries.Children.Add(new TextBlock
                {
                    Text = category.ToUpperInvariant(),
                    FontSize = 11,
                    FontWeight = Windows.UI.Text.FontWeights.SemiBold,
                    Margin = new Thickness(0, 6, 0, 2),
                    Foreground = (Windows.UI.Xaml.Media.SolidColorBrush)Application.Current.Resources["SystemControlForegroundBaseMediumBrush"]
                });
                var wrap = new StackPanel { Orientation = Orientation.Horizontal };
                foreach (var id in inputIds)
                {
                    var mapped = map.ContainsKey(id) ? map[id] : "None";
                    wrap.Children.Add(new Border
                    {
                        Margin = new Thickness(0, 0, 4, 2),
                        Padding = new Thickness(8, 4, 8, 4),
                        Child = new TextBlock { Text = GetInputLabel(id) + ": " + mapped, FontSize = 11 }
                    });
                }
                PreviewCategorySummaries.Children.Add(wrap);
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
            if (RemapView.Visibility == Visibility.Visible)
            {
                if (key == Windows.System.VirtualKey.GamepadB || key == Windows.System.VirtualKey.Escape)
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
                if (key == Windows.System.VirtualKey.GamepadLeftShoulder)
                { CycleBrowseSection(-1); e.Handled = true; }
                else if (key == Windows.System.VirtualKey.GamepadRightShoulder)
                { CycleBrowseSection(1); e.Handled = true; }
                else if (key == Windows.System.VirtualKey.GamepadB || key == Windows.System.VirtualKey.Escape)
                {
                    BrowseLayoutBack_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                }
                return;
            }

            if (key == Windows.System.VirtualKey.GamepadLeftTrigger)
            { CycleViewEditTab(-1); e.Handled = true; }
            else if (key == Windows.System.VirtualKey.GamepadRightTrigger)
            { CycleViewEditTab(1); e.Handled = true; }
            else if (key == Windows.System.VirtualKey.GamepadLeftShoulder)
            { CycleEditCategory(-1); e.Handled = true; }
            else if (key == Windows.System.VirtualKey.GamepadRightShoulder)
            { CycleEditCategory(1); e.Handled = true; }
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

            list.ItemClick += (s, args) =>
            {
                list.SelectedItem = args.ClickedItem;
            };

            var dialog = new ContentDialog
            {
                Title = title,
                Content = list,
                PrimaryButtonText = "Select",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                return -1;
            return list.SelectedIndex;
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
                    Padding = new Thickness(12, 6, 12, 6),
                    Margin = new Thickness(0, 0, 4, 0),
                    FontSize = 12
                };
                btn.Click += EditCategoryTab_Click;
                SetTabActive(btn, cat.Name == _activeEditCategory);
                EditCategoryTabs.Children.Add(btn);
            }
            RebuildEditCategoryContent();
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
                        Margin = new Thickness(0, 0, 0, 4),
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        HorizontalContentAlignment = HorizontalAlignment.Left,
                        Padding = new Thickness(12, 8, 12, 8)
                    };
                    var panel = new StackPanel();
                    panel.Children.Add(new TextBlock
                    {
                        Text = label,
                        FontSize = 11,
                        Foreground = (Windows.UI.Xaml.Media.SolidColorBrush)Application.Current.Resources["SystemControlForegroundBaseMediumBrush"]
                    });
                    panel.Children.Add(new TextBlock { Text = FormatModeLabel(current), FontSize = 14 });
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
                        Margin = new Thickness(0, 0, 0, 4)
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
                        Margin = new Thickness(0, 0, 0, 4)
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
                FontSize = 11,
                FontWeight = Windows.UI.Text.FontWeights.SemiBold,
                Foreground = (Windows.UI.Xaml.Media.SolidColorBrush)Application.Current.Resources["SystemControlForegroundBaseMediumBrush"],
                Margin = new Thickness(0, 8, 0, 4)
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
                FontSize = 13,
                Foreground = (Windows.UI.Xaml.Media.SolidColorBrush)Application.Current.Resources["SystemControlForegroundBaseHighBrush"]
            };
            Grid.SetColumn(nameBlock, 0);
            grid.Children.Add(nameBlock);

            var mappedBlock = new TextBlock
            {
                Text = mapped,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 12,
                Foreground = (Windows.UI.Xaml.Media.SolidColorBrush)Application.Current.Resources["SystemControlForegroundBaseMediumBrush"],
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Grid.SetColumn(mappedBlock, 1);
            grid.Children.Add(mappedBlock);

            var editBtn = new Button
            {
                Content = "Edit \u203A",
                Tag = inputId,
                Padding = new Thickness(10, 4, 10, 4),
                IsEnabled = !locked
            };
            editBtn.Click += EditCardButton_Click;
            Grid.SetColumn(editBtn, 2);
            grid.Children.Add(editBtn);

            var card = new Button
            {
                Content = grid,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 4),
                IsEnabled = !locked
            };
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

        void RemapTab_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button btn) || !(btn.Tag is string tab)) return;

            if (tab == "None")
            {
                _ = ApplyRemapAsync("none", "", "0");
                return;
            }

            _remapTab = tab;
            if (tab == "Keyboard")
                ApplyStickyModsFromCurrentBinding();
            else
                _stickyMods = 0;

            BuildRemapGrid();
            HighlightRemapTabs();
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
            if (isCurrent)
                btn.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
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
            var mapped = GetCurrentMappedDisplay();
            var currentIsNone = string.Equals(mapped, "None", StringComparison.OrdinalIgnoreCase);

            SetTabActive(RemapTabGamepad, _remapTab == "Gamepad");
            SetTabActive(RemapTabKeyboard, _remapTab == "Keyboard");
            SetTabActive(RemapTabMouse, _remapTab == "Mouse");
            SetTabActive(RemapTabNone, currentIsNone);
        }

        static void SetTabActive(Button btn, bool active)
        {
            btn.Style = active
                ? (Style)Application.Current.Resources["AccentButtonStyle"]
                : null;
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
                        Padding = new Thickness(14, 8, 14, 8),
                        Margin = new Thickness(0, 0, 4, 4),
                        MinWidth = 60
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
                        Padding = new Thickness(4, 6, 4, 6),
                        Margin = new Thickness(0, 0, 2, 0),
                        MinWidth = 28 * width,
                        HorizontalContentAlignment = HorizontalAlignment.Center
                    };
                    StyleAsCurrentBinding(btn, IsCurrentKeyBinding(vk));
                    btn.Click += RemapGridButton_Click;
                    wrap.Children.Add(btn);
                }
                RemapGrid.Children.Add(wrap);
            }
        }

        void AddModifierToggle(StackPanel panel, string label, int flag)
        {
            var toggle = new ToggleButton
            {
                Content = label,
                Padding = new Thickness(12, 6, 12, 6),
                Margin = new Thickness(0, 0, 4, 0)
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
                    Padding = new Thickness(14, 8, 14, 8),
                    Margin = new Thickness(0, 0, 4, 4),
                    MinWidth = 80
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

        // ═══════════════ View panel: controller layout labels ═══════════════

        void RebuildLayoutLabels()
        {
            LeftLabelsPanel.Children.Clear();
            RightLabelsPanel.Children.Clear();
            CategorySummaries.Children.Clear();

            if (!_controllerConnected || string.IsNullOrEmpty(_controllerModel))
                return;

            if (_controllerModel == "sc1")
                BuildSc1Labels();
            else
                BuildSc2Labels();

            foreach (var (category, inputIds) in LayoutCategories)
            {
                var header = new TextBlock
                {
                    Text = category.ToUpperInvariant(),
                    FontSize = 11,
                    FontWeight = Windows.UI.Text.FontWeights.SemiBold,
                    Foreground = (Windows.UI.Xaml.Media.SolidColorBrush)Application.Current.Resources["SystemControlForegroundBaseMediumBrush"],
                    Margin = new Thickness(0, 6, 0, 2)
                };
                CategorySummaries.Children.Add(header);

                var wrap = new StackPanel { Orientation = Orientation.Horizontal };
                foreach (var id in inputIds)
                {
                    var mapped = _inputMap.ContainsKey(id) ? _inputMap[id] : "None";
                    if (IsLockedGuideInput(id)) mapped = "Guide (locked)";

                    var label = GetInputLabel(id);
                    var btn = new Button
                    {
                        Content = label + ": " + mapped,
                        Tag = id,
                        Padding = new Thickness(8, 4, 8, 4),
                        Margin = new Thickness(0, 0, 4, 2),
                        FontSize = 11,
                        IsEnabled = !IsLockedGuideInput(id)
                    };
                    btn.Click += MappingLabel_Click;
                    wrap.Children.Add(btn);
                }
                CategorySummaries.Children.Add(wrap);
            }
        }

        void BuildSc2Labels()
        {
            // 2026 Steam Controller: sticks primary, smaller trackpads below, proper D-pad
            AddMappingLabel(LeftLabelsPanel, "Lb", "Left Bumper");
            AddMappingLabel(LeftLabelsPanel, "Lt", "Left Trigger");
            AddMappingLabel(LeftLabelsPanel, "L4", "L4 Grip");
            AddMappingLabel(LeftLabelsPanel, "L5", "L5 Grip");
            AddMappingLabel(LeftLabelsPanel, "View", "Select");
            AddMappingLabel(LeftLabelsPanel, "LeftStick", "Left Stick");
            AddMappingLabel(LeftLabelsPanel, "DpadUp", "DPad Up");
            AddMappingLabel(LeftLabelsPanel, "DpadDown", "DPad Down");
            AddMappingLabel(LeftLabelsPanel, "DpadLeft", "DPad Left");
            AddMappingLabel(LeftLabelsPanel, "DpadRight", "DPad Right");
            AddMappingLabel(LeftLabelsPanel, "LeftTrackpad", "Left Trackpad");

            AddMappingLabel(RightLabelsPanel, "Rb", "Right Bumper");
            AddMappingLabel(RightLabelsPanel, "Rt", "Right Trigger");
            AddMappingLabel(RightLabelsPanel, "R4", "R4 Grip");
            AddMappingLabel(RightLabelsPanel, "R5", "R5 Grip");
            AddMappingLabel(RightLabelsPanel, "Menu", "Start");
            AddMappingLabel(RightLabelsPanel, "Y", "Y");
            AddMappingLabel(RightLabelsPanel, "X", "X");
            AddMappingLabel(RightLabelsPanel, "B", "B");
            AddMappingLabel(RightLabelsPanel, "A", "A");
            AddMappingLabel(RightLabelsPanel, "RightStick", "Right Stick");
            AddMappingLabel(RightLabelsPanel, "RightTrackpad", "Right Trackpad");
        }

        void BuildSc1Labels()
        {
            // 2015 Steam Controller: large circular trackpads primary, single left stick below left pad
            AddMappingLabel(LeftLabelsPanel, "Lb", "Left Bumper");
            AddMappingLabel(LeftLabelsPanel, "Lt", "Left Trigger");
            AddMappingLabel(LeftLabelsPanel, "L4", "L4 Grip");
            AddMappingLabel(LeftLabelsPanel, "L5", "L5 Grip");
            AddMappingLabel(LeftLabelsPanel, "View", "Select");
            AddMappingLabel(LeftLabelsPanel, "LeftTrackpad", "Left Trackpad");
            AddMappingLabel(LeftLabelsPanel, "DpadUp", "Pad Up");
            AddMappingLabel(LeftLabelsPanel, "DpadDown", "Pad Down");
            AddMappingLabel(LeftLabelsPanel, "DpadLeft", "Pad Left");
            AddMappingLabel(LeftLabelsPanel, "DpadRight", "Pad Right");
            AddMappingLabel(LeftLabelsPanel, "LeftStick", "Left Stick");

            AddMappingLabel(RightLabelsPanel, "Rb", "Right Bumper");
            AddMappingLabel(RightLabelsPanel, "Rt", "Right Trigger");
            AddMappingLabel(RightLabelsPanel, "R4", "R4 Grip");
            AddMappingLabel(RightLabelsPanel, "R5", "R5 Grip");
            AddMappingLabel(RightLabelsPanel, "Menu", "Start");
            AddMappingLabel(RightLabelsPanel, "RightTrackpad", "Right Trackpad");
            AddMappingLabel(RightLabelsPanel, "Y", "Y");
            AddMappingLabel(RightLabelsPanel, "X", "X");
            AddMappingLabel(RightLabelsPanel, "B", "B");
            AddMappingLabel(RightLabelsPanel, "A", "A");
            AddMappingLabel(RightLabelsPanel, "RightStick", "Right Stick");
        }

        void AddMappingLabel(StackPanel panel, string inputId, string displayName)
        {
            var mapped = _inputMap.ContainsKey(inputId) ? _inputMap[inputId] : "";
            if (IsLockedGuideInput(inputId)) mapped = "Guide";
            var text = string.IsNullOrEmpty(mapped) || mapped == "None"
                ? displayName
                : displayName + " \u2192 " + mapped;

            var btn = new Button
            {
                Content = text,
                Tag = inputId,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(6, 3, 6, 3),
                Margin = new Thickness(0, 0, 0, 2),
                FontSize = 11,
                IsEnabled = !IsLockedGuideInput(inputId)
            };
            btn.Click += MappingLabel_Click;
            panel.Children.Add(btn);
        }

        void MappingLabel_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button btn) || !(btn.Tag is string id)) return;
            OpenRemapView(id);
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
                GameText.Text = string.IsNullOrEmpty(game)
                    ? "No foreground game \u00b7 profile source: " + source
                    : "Game: " + game + " \u00b7 " + activeName + " (" + source + ")";

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
