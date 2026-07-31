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

                foreach (var id in FilterInputsForModel(cat.InputIds))
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
    }
}
