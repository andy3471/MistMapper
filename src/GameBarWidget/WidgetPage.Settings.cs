using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

namespace MistMapper.GameBarWidget
{
    public sealed partial class WidgetPage
    {
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
                    Text = ModelDisplayName(pad.Model),
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

                var rumbleToggle = new ToggleSwitch
                {
                    Header = "Rumble",
                    IsOn = pad.RumbleEnabled,
                    Tag = pad.DeviceKey,
                    Margin = new Thickness(0, 10, 0, 0),
                    OffContent = "Off",
                    OnContent = "On"
                };
                rumbleToggle.Toggled += SettingsRumble_Toggled;
                card.Children.Add(rumbleToggle);

                SettingsControllerList.Children.Add(card);
            }
        }

        static string PadLabel(ControllerPadInfo pad)
        {
            if (!string.IsNullOrWhiteSpace(pad.DisplayName))
                return pad.DisplayName.Trim();
            return ModelDisplayName(pad.Model);
        }

        async void SettingsIdentify_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button btn) || !(btn.Tag is string key)) return;
            StatusText.Text = "Identifying…";
            var resp = await IpcClient.SendAsync("identifyController", key);
            StatusText.Text = resp.IsOk ? "Vibrated pad" : (resp.Error ?? "Identify failed");
        }

        async void SettingsRumble_Toggled(object sender, RoutedEventArgs e)
        {
            if (_suppress) return;
            if (!(sender is ToggleSwitch toggle) || !(toggle.Tag is string key)) return;
            var on = toggle.IsOn;
            var resp = await IpcClient.SendAsync("setControllerRumble", key + "\t" + (on ? "true" : "false"));
            if (!resp.IsOk)
            {
                _suppress = true;
                toggle.IsOn = !on;
                _suppress = false;
                StatusText.Text = resp.Error ?? "Rumble setting failed";
                return;
            }
            var pad = _controllers.FirstOrDefault(c =>
                string.Equals(c.DeviceKey, key, StringComparison.OrdinalIgnoreCase));
            if (pad != null) pad.RumbleEnabled = on;
            StatusText.Text = on ? "Rumble on" : "Rumble off";
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
                if (custom.Equals("Steam Controller 2", StringComparison.OrdinalIgnoreCase))
                    return "SC2";
                if (custom.Equals("Steam Controller", StringComparison.OrdinalIgnoreCase))
                    return pad.Model == "sc1" ? "SC1" : "SC";
                if (custom.Equals("DualSense Edge", StringComparison.OrdinalIgnoreCase))
                    return "Edge";
                if (custom.Equals("DualSense", StringComparison.OrdinalIgnoreCase))
                    return "DS";
                return custom.Length <= 14 ? custom : custom.Substring(0, 13) + "…";
            }
            return pad.Model switch
            {
                "sc1" => "SC1",
                "sc2" => "SC2",
                "dualsense" => "DS",
                "dualsense-edge" => "Edge",
                _ => "Pad"
            };
        }

        static string ModelDisplayName(string model) => model switch
        {
            "sc1" => "Steam Controller",
            "sc2" => "Steam Controller 2",
            "dualsense" => "DualSense",
            "dualsense-edge" => "DualSense Edge",
            _ => "Controller"
        };
        async void ModePicker_Click(object sender, RoutedEventArgs e)
        {
            if (_suppress || string.IsNullOrEmpty(_activeProfileId)) return;
            if (!(sender is Button btn) || !(btn.Tag is string tag)) return;

            var modes = tag == "gyro" ? GyroModes : TrackpadModes;
            var labels = modes.Select(FormatModeLabel).ToArray();
            var current = _modeValues.ContainsKey(tag) ? _modeValues[tag] : "Off";
            var title = tag == "gyro"
                ? "Gyro mode"
                : ModeSurfaceLabel(tag) + " mode";
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
            + "\"rotationDegrees\":" + _padRotation[side].ToString(CultureInfo.InvariantCulture) + ","
            + "\"mouseHaptics\":\"" + _mouseHaptics[side] + "\","
            + "\"flickSensitivity\":" + _flickSensitivity[side].ToString(CultureInfo.InvariantCulture)
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
                    var available = GyroActivationChoicesForModel()
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
            var mode = _modeValues.ContainsKey(side) ? _modeValues[side] : "Off";
            var draftTrackball = _trackballMode[side];
            var draftFriction = _trackballFriction[side];
            var draftVert = _padVertFriction[side];
            var draftSmooth = _padSmoothing[side];
            var draftRot = _padRotation[side];
            var draftHaptics = _mouseHaptics[side];
            var draftFlick = _flickSensitivity[side];

            bool isMouse = mode.Equals("AsMouse", StringComparison.OrdinalIgnoreCase);
            bool isMouseJoy = mode.Equals("AsMouseJoystick", StringComparison.OrdinalIgnoreCase);
            bool isFlick = mode.Equals("FlickStick", StringComparison.OrdinalIgnoreCase);
            bool isScroll = mode.Equals("ScrollWheel", StringComparison.OrdinalIgnoreCase);
            bool isStick = mode.Equals("AsLeftStick", StringComparison.OrdinalIgnoreCase)
                || mode.Equals("AsRightStick", StringComparison.OrdinalIgnoreCase);
            bool showTrackball = isMouse || isMouseJoy;
            bool showHaptics = isMouse || isMouseJoy;
            bool showFeel = isMouse || isMouseJoy || isScroll || isFlick || isStick;
            bool showFlick = isFlick;
            bool any = showTrackball || showHaptics || showFlick || showFeel;

            AdvancedSettingsContent.Children.Clear();
            AdvancedSettingsTitle.Text = ModeSurfaceLabel(side);
            AdvancedSettingsSubtitle.Text = FormatModeLabel(mode) + " · B cancel · Save when done";

            if (!any)
            {
                AdvancedSettingsContent.Children.Add(Hint(
                    "No advanced settings for " + FormatModeLabel(mode) + "."));
                if (!await ShowAdvancedSettingsSheetAsync())
                    return;
                return;
            }

            if (showTrackball)
            {
                AdvancedSettingsContent.Children.Add(SectionLabel(isMouseJoy ? "Return / linger" : "Trackball"));
                var trackball = MakePillToggle(
                    isMouseJoy ? "Linger after lift" : "Trackball Mode",
                    draftTrackball);
                trackball.Checked += (_, __) => draftTrackball = true;
                trackball.Unchecked += (_, __) => draftTrackball = false;
                AdvancedSettingsContent.Children.Add(trackball);
                AdvancedSettingsContent.Children.Add(Hint(isMouseJoy
                    ? "On: Trackball Friction controls how fast the virtual stick returns to center (also while touching)."
                    : "On: cursor keeps moving after you lift, slowed by Trackball Friction."));

                AdvancedSettingsContent.Children.Add(BuildChoicePillRow(
                    "Trackball Friction",
                    TrackballFrictions, TrackballFrictions.Select(FormatModeLabel).ToArray(),
                    draftFriction, v => draftFriction = v));
                AdvancedSettingsContent.Children.Add(Hint(isMouseJoy
                    ? "Lower = stick tip lingers longer. Higher = snappier return."
                    : "Lower = longer coast after lift. Higher = stops sooner."));
            }

            if (showHaptics)
            {
                AdvancedSettingsContent.Children.Add(SectionLabel("Mouse haptics"));
                AdvancedSettingsContent.Children.Add(BuildChoicePillRow(
                    "Intensity",
                    MouseHapticsIntensities, MouseHapticsIntensities.Select(FormatModeLabel).ToArray(),
                    draftHaptics, v => draftHaptics = v));
                AdvancedSettingsContent.Children.Add(Hint("Ticks while sliding on the pad."));
            }

            if (showFlick)
            {
                AdvancedSettingsContent.Children.Add(SectionLabel("Flick Stick"));
                var flick = MakeStyledSlider("Flick Sensitivity %", 10, 300, 5, draftFlick * 100);
                flick.ValueChanged += (_, ev) => draftFlick = ev.NewValue / 100.0;
                AdvancedSettingsContent.Children.Add(flick);
                AdvancedSettingsContent.Children.Add(Hint("Yaw from pad arc when you flick and lift."));
            }

            if (showFeel)
            {
                AdvancedSettingsContent.Children.Add(SectionLabel("Feel"));
                if (isMouse || isMouseJoy)
                {
                    var vFric = MakeStyledSlider("Vertical Friction Scale %", 10, 300, 5, draftVert * 100);
                    vFric.ValueChanged += (_, ev) => draftVert = ev.NewValue / 100.0;
                    AdvancedSettingsContent.Children.Add(vFric);
                    AdvancedSettingsContent.Children.Add(Hint(isMouseJoy
                        ? "Higher stops vertical stick tip sooner than horizontal."
                        : "Higher stops up/down coast sooner (good for camera yaw)."));
                }

                if (isMouse || isMouseJoy || isScroll || isFlick)
                {
                    var smooth = MakeStyledSlider("Smoothing", 0, 100, 1, draftSmooth);
                    smooth.ValueChanged += (_, ev) => draftSmooth = ev.NewValue;
                    AdvancedSettingsContent.Children.Add(smooth);
                    AdvancedSettingsContent.Children.Add(Hint("Higher removes jitter but adds lag."));
                }

                var rot = MakeStyledSlider("Rotation (°)", -45, 45, 1, draftRot);
                rot.ValueChanged += (_, ev) => draftRot = ev.NewValue;
                AdvancedSettingsContent.Children.Add(rot);
                AdvancedSettingsContent.Children.Add(Hint("Cant pad axes to match a natural thumb swipe."));
            }

            if (!await ShowAdvancedSettingsSheetAsync())
                return;

            _trackballMode[side] = draftTrackball;
            _trackballFriction[side] = draftFriction;
            _padVertFriction[side] = draftVert;
            _padSmoothing[side] = draftSmooth;
            _padRotation[side] = draftRot;
            _mouseHaptics[side] = draftHaptics;
            _flickSensitivity[side] = draftFlick;
            await SendSensitivityAsync();
        }

        static bool TrackpadModeHasAdvancedSettings(string mode) =>
            mode.Equals("AsMouse", StringComparison.OrdinalIgnoreCase)
            || mode.Equals("AsMouseJoystick", StringComparison.OrdinalIgnoreCase)
            || mode.Equals("FlickStick", StringComparison.OrdinalIgnoreCase)
            || mode.Equals("ScrollWheel", StringComparison.OrdinalIgnoreCase)
            || mode.Equals("AsLeftStick", StringComparison.OrdinalIgnoreCase)
            || mode.Equals("AsRightStick", StringComparison.OrdinalIgnoreCase);

        static bool TrackpadModeUsesDeadzone(string mode) =>
            mode.Equals("AsLeftStick", StringComparison.OrdinalIgnoreCase)
            || mode.Equals("AsRightStick", StringComparison.OrdinalIgnoreCase)
            || mode.Equals("AsDpad", StringComparison.OrdinalIgnoreCase)
            || mode.Equals("ButtonPad", StringComparison.OrdinalIgnoreCase)
            || mode.Equals("FlickStick", StringComparison.OrdinalIgnoreCase);

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

        string GyroButtonLabel(string id)
        {
            foreach (var (choiceId, label) in GyroActivationChoicesForModel())
            {
                if (choiceId.Equals(id, StringComparison.OrdinalIgnoreCase))
                    return label;
            }
            // Fallbacks for saved ids that aren't offered on this model.
            if (id.Equals("L4", StringComparison.OrdinalIgnoreCase) && _controllerModel == "sc1")
                return "Left Grip";
            if (id.Equals("R4", StringComparison.OrdinalIgnoreCase) && _controllerModel == "sc1")
                return "Right Grip";
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
    }
}
