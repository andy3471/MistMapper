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

namespace MistMapper.GameBarWidget
{
    public sealed partial class WidgetPage
    {
        // ═══════════════ Remap view event handlers ═══════════════

        string GetCurrentMappedDisplay()
        {
            if (string.IsNullOrEmpty(_remapInputId))
                return "None";

            if (_remapPickMode)
            {
                foreach (var (activator, slot, display) in GetCommandRows(_remapInputId))
                {
                    if (string.Equals(activator, _remapActivator, StringComparison.OrdinalIgnoreCase)
                        && slot == _remapSlot)
                        return string.IsNullOrEmpty(display) ? "None" : display;
                }
                return "None";
            }

            if (!_inputMap.ContainsKey(_remapInputId))
                return "None";
            var mapped = _inputMap[_remapInputId];
            return string.IsNullOrEmpty(mapped) ? "None" : mapped;
        }

        int CountActivatorActions(string inputId, string activator)
        {
            if (!_bindingsByInput.TryGetValue(inputId, out var groups))
                return 0;
            foreach (var g in groups)
            {
                if (string.Equals(g.Activator, activator, StringComparison.OrdinalIgnoreCase))
                    return g.Actions.Count;
            }
            return 0;
        }

        IEnumerable<(string Activator, int Slot, string Display)> GetCommandRows(string inputId)
        {
            if (!_bindingsByInput.TryGetValue(inputId, out var groups))
                yield break;

            foreach (var g in groups
                .OrderBy(x => string.Equals(x.Activator, "LongPress", StringComparison.OrdinalIgnoreCase) ? 1 : 0))
            {
                for (var i = 0; i < g.Actions.Count; i++)
                    yield return (g.Activator, i, g.Actions[i]);
            }
        }

        static readonly string[] RemapTabOrder = { "Gamepad", "Keyboard", "Mouse", "None" };

        void RemapTab_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button btn) || !(btn.Tag is string tab)) return;
            SetRemapTab(tab);
        }

        void CycleRemapTab(int delta)
        {
            if (RemapView.Visibility != Visibility.Visible || !_remapPickMode) return;
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

            RebuildRemapUi();
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
            _remapPickMode = false;
            _remapActivator = "Regular";
            _remapSlot = 0;

            MainView.Visibility = Visibility.Collapsed;
            RemapView.Visibility = Visibility.Visible;
            RebuildRemapUi();
        }

        void EnterRemapPickMode(string activator, int slot)
        {
            _remapPickMode = true;
            _remapActivator = activator;
            _remapSlot = Math.Clamp(slot, 0, 1);
            ResolveRemapTabFromCurrentBinding();
            RebuildRemapUi();
        }

        void RemapCancel_Click(object sender, RoutedEventArgs e)
        {
            if (_remapPickMode)
            {
                _remapPickMode = false;
                RebuildRemapUi();
                return;
            }

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


        void RebuildRemapUi()
        {
            if (_remapPickMode)
                BuildRemapPicker();
            else
                BuildRemapCommandList();
        }

        void BuildRemapCommandList()
        {
            RemapGrid.Children.Clear();
            RemapTabsRow.Visibility = Visibility.Collapsed;

            var label = GetInputLabel(_remapInputId);
            RemapTitle.Text = "Commands for " + label;
            RemapSubtitle.Text = "Steam-style: one command, then optional sub commands / long press.";
            RemapHint.Text = "Back arrow returns to Edit";

            var rows = GetCommandRows(_remapInputId).ToList();
            if (rows.Count == 0)
            {
                RemapGrid.Children.Add(Hint("No commands yet. Add a command to start."));
            }
            else
            {
                RemapGrid.Children.Add(SectionLabel("Bound commands"));
                foreach (var row in rows)
                    RemapGrid.Children.Add(BuildCommandRow(row.Activator, row.Slot, row.Display));
            }

            var regularCount = CountActivatorActions(_remapInputId, "Regular");
            var longCount = CountActivatorActions(_remapInputId, "LongPress");

            RemapGrid.Children.Add(SectionLabel("Add"));
            if (regularCount < 2)
            {
                var addLabel = regularCount == 0 ? "Add command" : "Add sub command";
                RemapGrid.Children.Add(BuildAddCommandButton(addLabel, "Regular", regularCount));
            }
            if (longCount < 2)
            {
                var addLabel = longCount == 0 ? "Add long press" : "Add long press sub command";
                RemapGrid.Children.Add(BuildAddCommandButton(addLabel, "LongPress", longCount));
            }
            if (regularCount >= 2 && longCount >= 2)
                RemapGrid.Children.Add(Hint("This input already has the maximum of two regular and two long-press commands."));
            else
                RemapGrid.Children.Add(Hint("Long press fires after ~400 ms and replaces regular while held."));
        }

        UIElement BuildCommandRow(string activator, int slot, string display)
        {
            var isLong = string.Equals(activator, "LongPress", StringComparison.OrdinalIgnoreCase);
            var kindLabel = isLong
                ? (slot == 0 ? "Long press" : "Long press · sub")
                : (slot == 0 ? "Command" : "Sub command");

            var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            textStack.Children.Add(new TextBlock
            {
                Text = kindLabel,
                FontSize = 12,
                Foreground = (Windows.UI.Xaml.Media.SolidColorBrush)Application.Current.Resources["SystemControlForegroundBaseMediumBrush"]
            });
            textStack.Children.Add(new TextBlock
            {
                Text = display,
                FontSize = 15,
                FontWeight = Windows.UI.Text.FontWeights.SemiBold,
                Foreground = (Windows.UI.Xaml.Media.SolidColorBrush)Application.Current.Resources["SystemControlForegroundBaseHighBrush"]
            });

            var editBtn = new Button
            {
                Content = textStack,
                Tag = activator + "\t" + slot.ToString(CultureInfo.InvariantCulture),
                Style = (Style)Application.Current.Resources["PillButtonStyle"],
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(16, 12, 16, 12),
                Margin = new Thickness(0, 0, 8, 0)
            };
            editBtn.Click += (s, e) =>
            {
                if (!(s is Button b) || !(b.Tag is string tag)) return;
                var parts = tag.Split('\t');
                if (parts.Length != 2) return;
                if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var rowSlot))
                    return;
                EnterRemapPickMode(parts[0], rowSlot);
            };
            Grid.SetColumn(editBtn, 0);
            grid.Children.Add(editBtn);

            var removeBtn = new Button
            {
                Content = "Remove",
                Tag = activator + "\t" + slot.ToString(CultureInfo.InvariantCulture),
                Style = (Style)Application.Current.Resources["PillButtonStyle"],
                Padding = new Thickness(14, 12, 14, 12),
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            removeBtn.Click += async (s, e) =>
            {
                if (!(s is Button b) || !(b.Tag is string tag)) return;
                var parts = tag.Split('\t');
                if (parts.Length != 2) return;
                if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var rowSlot))
                    return;
                _remapActivator = parts[0];
                _remapSlot = rowSlot;
                await ApplyBindingAsync("none", "", "0", returnToList: true);
            };
            Grid.SetColumn(removeBtn, 1);
            grid.Children.Add(removeBtn);

            return grid;
        }

        Button BuildAddCommandButton(string label, string activator, int slot)
        {
            var btn = new Button
            {
                Content = label,
                Style = (Style)Application.Current.Resources["PillAccentButtonStyle"],
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(16, 12, 16, 12),
                Margin = new Thickness(0, 0, 0, 8),
                FontSize = 14
            };
            btn.Click += (s, e) => EnterRemapPickMode(activator, slot);
            return btn;
        }

        void BuildRemapPicker()
        {
            RemapGrid.Children.Clear();
            RemapTabsRow.Visibility = Visibility.Visible;

            var label = GetInputLabel(_remapInputId);
            var mapped = GetCurrentMappedDisplay();
            var isLong = string.Equals(_remapActivator, "LongPress", StringComparison.OrdinalIgnoreCase);
            var slotLabel = isLong
                ? (_remapSlot == 0 ? "Long press" : "Long press · sub command")
                : (_remapSlot == 0 ? "Command" : "Sub command");

            RemapTitle.Text = "Select a command for " + label;
            RemapSubtitle.Text = slotLabel + " · Currently: " + mapped;
            RemapHint.Text = "X / B switch type · Back arrow cancels";

            HighlightRemapTabs();

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
                Text = "Remove the current binding for this command slot.",
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
            clear.Click += async (s, e) =>
            {
                await ApplyBindingAsync("none", "", "0", returnToList: true);
            };
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
                    RebuildRemapUi();
            };
            toggle.Unchecked += (s, _) =>
            {
                _stickyMods &= ~flag;
                if (_remapTab == "Keyboard")
                    RebuildRemapUi();
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

            await ApplyBindingAsync(kind, value, mods, returnToList: true);
        }

        async Task ApplyRemapAsync(string kind, string value, string mods) =>
            await ApplyBindingAsync(kind, value, mods, returnToList: true);

        async Task ApplyBindingAsync(string kind, string value, string mods, bool returnToList = true)
        {
            if (string.IsNullOrEmpty(_remapInputId) || string.IsNullOrEmpty(_activeProfileId))
                return;

            if (!await EnsureSharedBindingsEditAllowedAsync())
                return;

            // profileId \t inputId \t activator \t slot \t kind \t value [\t modifiers]
            var payload = _activeProfileId + "\t" + _remapInputId + "\t" + _remapActivator + "\t"
                + _remapSlot.ToString(CultureInfo.InvariantCulture) + "\t" + kind + "\t" + value + "\t" + mods;
            var resp = await IpcClient.SendAsync("setBinding", payload);
            StatusText.Text = resp.IsOk
                ? (_remapInputId + (kind == "none" ? " cleared" : " remapped to " + value))
                : (resp.Error ?? "Remap failed");

            if (!resp.IsOk)
                return;

            _remapPickMode = false;
            await RefreshAsync(force: true);

            if (returnToList && RemapView.Visibility == Visibility.Visible)
                RebuildRemapUi();
            else
            {
                RemapView.Visibility = Visibility.Collapsed;
                MainView.Visibility = Visibility.Visible;
            }
        }
    }
}
