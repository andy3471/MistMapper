using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace SteamControllerBridge.GameBarWidget
{
    public sealed partial class WidgetPage : Page
    {
        static readonly string[] XboxOutputs =
        {
            "None", "A", "B", "X", "Y", "Lb", "Rb", "Back", "Start",
            "Guide", "LsClick", "RsClick", "Lt", "Rt",
            "DpadUp", "DpadDown", "DpadLeft", "DpadRight"
        };

        readonly DispatcherTimer _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        bool _suppress;
        bool _busy;
        string _activeProfileId = "";
        string _lastState;

        public WidgetPage()
        {
            InitializeComponent();
            foreach (var combo in new[] { L4Combo, L5Combo, R4Combo, R5Combo })
            {
                combo.Items.Clear();
                foreach (var o in XboxOutputs)
                    combo.Items.Add(o);
            }
            _timer.Tick += async (_, __) => await RefreshAsync();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _timer.Start();
            await RefreshAsync(force: true);
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            _timer.Stop();
            base.OnNavigatedFrom(e);
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

        async void ProfilesCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppress || !(ProfilesCombo.SelectedItem is string name)) return;
            var resp = await IpcClient.SendAsync("setActiveProfileByName", name);
            if (!resp.IsOk)
                StatusText.Text = resp.Error ?? "Profile switch failed";
            else
                await RefreshAsync(force: true);
        }

        async void PaddleCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppress || !(sender is ComboBox combo) || !(combo.SelectedItem is string xbox))
                return;
            var physical = combo.Tag as string;
            if (string.IsNullOrEmpty(physical) || string.IsNullOrEmpty(_activeProfileId))
                return;

            var payload = _activeProfileId + "\t" + physical + "\t" + xbox;
            var resp = await IpcClient.SendAsync("remapButton", payload);
            if (!resp.IsOk)
                StatusText.Text = resp.Error ?? "Remap failed";
            else
                StatusText.Text = physical + " → " + xbox;
        }

        async Task RefreshAsync(bool force = false)
        {
            if (_busy) return;
            _busy = true;
            try
            {
                var json = await IpcClient.ReadStateAsync();
                if (string.IsNullOrWhiteSpace(json) || !JsonObject.TryParse(json, out var state))
                {
                    StatusText.Text = "Host not running. Start SteamControllerBridge.exe (tray).";
                    return;
                }

                if (!force && string.Equals(json, _lastState, StringComparison.Ordinal))
                {
                    StatusText.Text = "Connected.";
                    return;
                }
                _lastState = json;
                Bind(state);
                StatusText.Text = "Connected.";
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

        void Bind(JsonObject state)
        {
            _suppress = true;
            try
            {
                var status = state.GetNamedString("statusMessage", "Connected");
                var bridgeOn = state.GetNamedBoolean("bridgeEnabled", true);
                _activeProfileId = state.GetNamedString("activeProfileId", "");
                var activeName = state.GetNamedString("activeProfileName", "");

                BridgeToggle.IsOn = bridgeOn;
                StatusText.Text = status;

                ProfilesCombo.Items.Clear();
                if (state.ContainsKey("profiles"))
                {
                    foreach (var item in state.GetNamedArray("profiles"))
                    {
                        var obj = item.GetObject();
                        ProfilesCombo.Items.Add(obj.GetNamedString("name"));
                    }
                }
                ProfilesCombo.SelectedItem = ProfilesCombo.Items
                    .Cast<object>()
                    .Select(o => o as string)
                    .FirstOrDefault(n => string.Equals(n, activeName, StringComparison.OrdinalIgnoreCase));

                var map = state.ContainsKey("paddleMap") ? state.GetNamedObject("paddleMap") : null;
                SetPaddle(L4Combo, map, "L4");
                SetPaddle(L5Combo, map, "L5");
                SetPaddle(R4Combo, map, "R4");
                SetPaddle(R5Combo, map, "R5");
            }
            finally
            {
                _suppress = false;
            }
        }

        static void SetPaddle(ComboBox combo, JsonObject map, string key)
        {
            var value = map != null && map.ContainsKey(key) ? map.GetNamedString(key, "None") : "None";
            combo.SelectedItem = XboxOutputs.Contains(value) ? value : "None";
        }
    }
}
