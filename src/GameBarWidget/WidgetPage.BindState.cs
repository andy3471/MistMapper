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
using Windows.Storage;

namespace MistMapper.GameBarWidget
{
    public sealed partial class WidgetPage
    {
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

        void ApplyControllersFromDto(HostStateDto dto)
        {
            _controllers.Clear();
            foreach (var entry in dto.Controllers)
            {
                _controllers.Add(new ControllerPadInfo
                {
                    DeviceKey = entry.DeviceKey,
                    Model = entry.Model,
                    DisplayName = entry.DisplayName,
                    ProfileId = entry.ProfileId,
                    Order = entry.Order,
                    Connected = entry.Connected,
                    HasProfileOverride = entry.HasProfileOverride,
                    RumbleEnabled = entry.RumbleEnabled
                });
            }

            _selectedDeviceKey = dto.SelectedDeviceKey;
            if (string.IsNullOrEmpty(_selectedDeviceKey) && _controllers.Count > 0)
                _selectedDeviceKey = _controllers.OrderBy(c => c.Order).First().DeviceKey;
        }

        void ApplyBindingsFromDto(HostStateDto dto)
        {
            _bindingsByInput.Clear();
            foreach (var kv in dto.BindingsByInput)
                _bindingsByInput[kv.Key] = kv.Value;
        }

        void ApplyTrackpadSettingsFromDto(HostStateDto.TrackpadSettingsEntry source, string side)
        {
            _trackballMode[side] = source.TrackballMode;
            _trackballFriction[side] = source.TrackballFriction;
            _padVertFriction[side] = source.VerticalFrictionScale;
            _padSmoothing[side] = source.Smoothing;
            _padRotation[side] = source.RotationDegrees;
            _mouseHaptics[side] = source.MouseHaptics;
            _flickSensitivity[side] = source.FlickSensitivity;
        }

        void Bind(JsonObject state, bool force)
        {
            if (!HostStateDto.TryParse(state, out var dto))
                return;

            _suppress = true;
            try
            {
                _activeProfileId = dto.ActiveProfileId;
                _selectedHasProfileOverride = dto.SelectedHasProfileOverride;
                ApplyControllersFromDto(dto);
                RebuildControllerStrips(force);

                var model = dto.ControllerModel;
                var selectedPad = _controllers.FirstOrDefault(c =>
                    string.Equals(c.DeviceKey, _selectedDeviceKey, StringComparison.OrdinalIgnoreCase));
                if (selectedPad != null && !string.IsNullOrEmpty(selectedPad.Model))
                    model = selectedPad.Model;

                var connectedChanged = dto.ControllerConnected != _controllerConnected;
                var modelChanged = !string.Equals(_controllerModel, model, StringComparison.OrdinalIgnoreCase);
                _controllerConnected = dto.ControllerConnected;
                _controllerModel = model ?? "";
                if (connectedChanged || modelChanged)
                {
                    ApplyControllerOutline();
                    if (_controllerConnected)
                        RebuildLayoutLabels();
                }

                BridgeToggle.IsOn = dto.BridgeEnabled;
                PauseSteamToggle.IsOn = dto.AutoPauseWhenSteam;
                StatusText.Text = dto.StatusMessage;
                if (string.IsNullOrEmpty(dto.CurrentGameExe))
                {
                    GameText.Text = "No game \u00b7 " + dto.ActiveProfileName;
                }
                else
                {
                    var forGame = dto.ActiveProfileSource.Equals("GameRule", StringComparison.OrdinalIgnoreCase);
                    GameText.Text = forGame
                        ? "Playing " + dto.CurrentGameName + " \u00b7 layout " + dto.ActiveProfileName
                        : "Playing " + dto.CurrentGameName + " \u00b7 " + dto.ActiveProfileName + " (not bound yet \u2014 edit to create)";
                }

                if (dto.HasGameIcon && !string.IsNullOrEmpty(dto.GameIconToken))
                {
                    GameIconBorder.Visibility = Visibility.Visible;
                    if (!string.Equals(dto.GameIconToken, _lastGameIconToken, StringComparison.Ordinal))
                    {
                        _lastGameIconToken = dto.GameIconToken;
                        _ = LoadGameIconAsync();
                    }
                }
                else
                {
                    GameIconBorder.Visibility = Visibility.Collapsed;
                    GameIconImage.Source = null;
                    _lastGameIconToken = "";
                }

                if (dto.DependencyError || !dto.ViiperOk)
                {
                    var msg = !dto.ViiperOk
                        ? (string.IsNullOrEmpty(dto.ViiperDetail) ? "VIIPER is required but not available." : dto.ViiperDetail)
                        : "";
                    if (dto.DependencyError && dto.ViiperOk)
                        msg = "Dependency error. Check host logs.";
                    ShowAlert(msg);
                }
                else if (!dto.ControllerConnected)
                {
                    ShowAlert("Controller not connected.");
                }
                else
                {
                    HideAlert();
                }

                OverrideBanner.Visibility = dto.GameBarOverrideActive ? Visibility.Visible : Visibility.Collapsed;

                _profileNames.Clear();
                _profileNames.AddRange(dto.ProfileNames);
                _selectedProfileName = dto.ActiveProfileName ?? "";
                _activeLayoutId = dto.ActiveLayoutId;
                _activeLayoutName = dto.ActiveLayoutName;
                CurrentLayoutTitle.Text = string.IsNullOrEmpty(_selectedProfileName) ? "—" : _selectedProfileName;
                CurrentLayoutSubtitle.Text = string.IsNullOrEmpty(_activeLayoutName)
                    ? "Custom mappings"
                    : "Based on " + _activeLayoutName;

                _officialLayouts.Clear();
                _officialLayouts.AddRange(dto.OfficialLayouts);
                _layoutsReady = dto.LayoutsReady;

                _modeValues["left"] = dto.LeftTrackpadMode;
                _modeValues["right"] = dto.RightTrackpadMode;
                _modeValues["gyro"] = dto.GyroMode;

                foreach (var kv in dto.SensValues)
                    _sensValues[kv.Key] = kv.Value;

                foreach (var kv in dto.InvertValues)
                    _invertValues[kv.Key] = kv.Value;

                _gyroButtonMode = dto.GyroButtonMode;
                _gyroButtonCombine = dto.GyroButtonCombine;
                _gyroButtons.Clear();
                _gyroButtons.AddRange(dto.GyroButtons);
                ApplyTrackpadSettingsFromDto(dto.LeftTrackpadSettings, "left");
                ApplyTrackpadSettingsFromDto(dto.RightTrackpadSettings, "right");

                _inputMap.Clear();
                foreach (var kv in dto.InputMap)
                    _inputMap[kv.Key] = kv.Value;

                ApplyBindingsFromDto(dto);

                var layoutChanged = false;
                if (dto.Layout.Count > 0)
                {
                    var key = dto.LayoutCacheKey;
                    if (!string.Equals(key, _lastLayoutKey, StringComparison.Ordinal) || force || _layout.Count == 0)
                    {
                        _layout.Clear();
                        _remappable.Clear();
                        foreach (var obj in dto.Layout)
                        {
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
                var mapKey = dto.InputMapCacheKey;
                var mapChanged = !string.Equals(mapKey, _lastInputMapKey, StringComparison.Ordinal);
                if (force || layoutChanged || mapChanged)
                {
                    _lastInputMapKey = mapKey;
                    RebuildLayoutLabels();
                    if (_activeViewTab == "Edit")
                        RebuildEditCategoryContent();
                    if (RemapView.Visibility == Visibility.Visible && !_remapPickMode)
                        RebuildRemapUi();
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
