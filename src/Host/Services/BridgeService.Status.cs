using MistMapper.Host.Drivers;
using MistMapper.Host.Steam;
using MistMapper.Shared;

namespace MistMapper.Host.Services;

public sealed partial class BridgeService
{
    bool IsGameBarOverrideActive()
    {
        // Use overlay-window visibility (GameBarWatcher), not widget heartbeat.
        // Heartbeat / widget process can stay "alive" after Win+G is dismissed and would
        // otherwise stick the bridge in stock Xbox mode.
        return _gameBar.IsGameBarOpen;
    }

    static void ApplyGameBarOverrideSurfaces(ControllerProfile source, ControllerProfile overlay)
    {
        overlay.Gyro = source.Gyro;
        overlay.GyroSensitivity = source.GyroSensitivity;
        overlay.GyroSensitivityX = source.GyroSensitivityX;
        overlay.GyroSensitivityY = source.GyroSensitivityY;
        overlay.GyroDotsPer360 = source.GyroDotsPer360;
        overlay.InvertGyroX = source.InvertGyroX;
        overlay.InvertGyroY = source.InvertGyroY;
        overlay.GyroButtons = source.GyroButtons.ToList();
        overlay.GyroButtonMode = source.GyroButtonMode;
        overlay.GyroButtonCombine = source.GyroButtonCombine;
        overlay.LeftTrackpad = source.LeftTrackpad;
        overlay.RightTrackpad = source.RightTrackpad;
        overlay.LeftTrackpadSettings = source.LeftTrackpadSettings;
        overlay.RightTrackpadSettings = source.RightTrackpadSettings;
        overlay.TrackpadSensitivityX = source.TrackpadSensitivityX;
        overlay.TrackpadSensitivityY = source.TrackpadSensitivityY;
        overlay.TrackpadDeadzone = source.TrackpadDeadzone;
        overlay.InvertTrackpadX = source.InvertTrackpadX;
        overlay.InvertTrackpadY = source.InvertTrackpadY;
    }

    void PublishStatus(BridgeRunState? state = null, string? message = null)
    {
        BridgeStatus snap;
        lock (_gate)
        {
            if (state.HasValue) _status.State = state.Value;
            if (message is not null) _status.Message = message;
            _status.BridgeEnabled = _profiles.BridgeEnabled;
            _status.AutoPauseWhenSteamRunning = _profiles.AutoPauseWhenSteamRunning;

            var connected = _slots.Where(s => s.Driver.IsConnected).OrderBy(s => s.Order).ToList();
            var primary = connected.FirstOrDefault() ?? PrimarySlotUnlocked();
            _status.ControllerConnected = connected.Count > 0;
            _status.SteamRunning = _steam.IsSteamRunning;
            _status.SessionLocked = _session.IsLocked;
            _status.ViiperConnected = _slots.Any(s => s.Viiper?.IsConnected == true);
            _status.GameBarOverrideActive = IsGameBarOverrideActive();

            var selected = FindSlotUnlocked(_selectedDeviceKey) ?? primary;
            var selectedResolved = selected is null
                ? (_resolvedProfile ?? _profiles.ActiveProfile)
                : ResolveProfileForSlot(selected.DeviceKey, selected.ProfileId).profile;
            var selectedSource = selected?.ProfileSource ?? _profileSource;

            _status.ActiveProfileId = selectedResolved.Id;
            _status.ActiveProfileName = selectedResolved.Name;
            _status.ActiveProfileSource = selectedSource.ToString();
            _status.ActiveDriverId = selected?.Driver.Id ?? primary?.Driver.Id ?? DriverIds.SteamController;
            _status.ActiveDriverName = selected?.DisplayName
                ?? selected?.Driver.DisplayName
                ?? primary?.DisplayName
                ?? primary?.Driver.DisplayName
                ?? "Steam Controller";
            _status.ControllerModel = selected?.Model
                ?? primary?.Model
                ?? "";
            _status.CurrentGameExe = _foreground.ExeName;
            _status.CurrentGamePath = _foreground.Path;
            _status.CurrentGameName = string.IsNullOrWhiteSpace(_foreground.DisplayName)
                ? GameDisplayName.Resolve(_foreground.Path, _foreground.ExeName)
                : _foreground.DisplayName;
            _status.PressedInputs = selected?.Pressed.ToList()
                ?? primary?.Pressed.ToList()
                ?? [];
            _status.SelectedDeviceKey = selected?.DeviceKey ?? _selectedDeviceKey;
            _status.Controllers = connected.Select(s =>
            {
                var (prof, _, hasOverride) = ResolveProfileForSlot(s.DeviceKey, s.ProfileId);
                return new ControllerStatus
                {
                    DeviceKey = s.DeviceKey,
                    Model = s.Model,
                    DisplayName = string.IsNullOrWhiteSpace(s.DisplayName)
                        ? DisplayNameForModel(s.Model)
                        : s.DisplayName,
                    Order = s.Order,
                    Enabled = s.Enabled,
                    RumbleEnabled = s.RumbleEnabled,
                    Connected = true,
                    ProfileId = prof.Id,
                    ProfileName = prof.Name,
                    HasProfileOverride = hasOverride || !string.IsNullOrEmpty(s.ProfileId),
                    PressedInputs = s.Pressed.ToList()
                };
            }).ToList();

            _status.Dependencies =
            [
                new DependencyStatus
                {
                    Id = _viiperDep.Id,
                    DisplayName = _viiperDep.DisplayName,
                    Ok = _viiperDep.Ok,
                    Detail = _viiperDep.Detail
                }
            ];
            _status.UpdatedAt = DateTimeOffset.UtcNow;
            if (!string.IsNullOrEmpty(_lastError) && _status.State == BridgeRunState.Error && message is null)
                _status.Message = _lastError;
            snap = CloneStatus(_status);
        }
        StatusChanged?.Invoke(snap);
    }

    static BridgeStatus CloneStatus(BridgeStatus s) => new()
    {
        State = s.State,
        BridgeEnabled = s.BridgeEnabled,
        AutoPauseWhenSteamRunning = s.AutoPauseWhenSteamRunning,
        ControllerConnected = s.ControllerConnected,
        ControllerModel = s.ControllerModel,
        SteamRunning = s.SteamRunning,
        SessionLocked = s.SessionLocked,
        ViiperConnected = s.ViiperConnected,
        GameBarOverrideActive = s.GameBarOverrideActive,
        ActiveProfileId = s.ActiveProfileId,
        ActiveProfileName = s.ActiveProfileName,
        ActiveProfileSource = s.ActiveProfileSource,
        ActiveDriverId = s.ActiveDriverId,
        ActiveDriverName = s.ActiveDriverName,
        CurrentGameExe = s.CurrentGameExe,
        CurrentGamePath = s.CurrentGamePath,
        CurrentGameName = s.CurrentGameName,
        PressedInputs = s.PressedInputs.ToList(),
        SelectedDeviceKey = s.SelectedDeviceKey,
        Controllers = s.Controllers.Select(c => new ControllerStatus
        {
            DeviceKey = c.DeviceKey,
            Model = c.Model,
            DisplayName = c.DisplayName,
            Order = c.Order,
            Enabled = c.Enabled,
            Connected = c.Connected,
            RumbleEnabled = c.RumbleEnabled,
            ProfileId = c.ProfileId,
            ProfileName = c.ProfileName,
            HasProfileOverride = c.HasProfileOverride,
            PressedInputs = c.PressedInputs.ToList()
        }).ToList(),
        Dependencies = s.Dependencies.Select(d => new DependencyStatus
        {
            Id = d.Id,
            DisplayName = d.DisplayName,
            Ok = d.Ok,
            Detail = d.Detail
        }).ToList(),
        Message = s.Message,
        UpdatedAt = s.UpdatedAt
    };

    static string DisplayNameForModel(string? model) => model switch
    {
        "sc1" => SteamControllerDevice.DisplayNameForModel("sc1"),
        "sc2" => SteamControllerDevice.DisplayNameForModel("sc2"),
        "dualsense" or "dualsense-edge" => DualSense.DualSenseDevice.DisplayNameForModel(model!),
        _ => SteamControllerDevice.DisplayNameForModel(model ?? "")
    };
}
