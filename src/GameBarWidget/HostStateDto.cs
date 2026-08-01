using System;
using System.Collections.Generic;
using System.Linq;
using Windows.Data.Json;

namespace MistMapper.GameBarWidget
{
    public sealed class HostStateDto
    {
        public sealed class ControllerEntry
        {
            public string DeviceKey = "";
            public string Model = "";
            public string DisplayName = "";
            public string ProfileId = "";
            public int Order;
            public bool Connected = true;
            public bool HasProfileOverride;
            public bool RumbleEnabled = true;
        }

        public sealed class TrackpadSettingsEntry
        {
            public bool TrackballMode;
            public string TrackballFriction = "High";
            public double VerticalFrictionScale = 1;
            public double Smoothing = 20;
            public double RotationDegrees;
            public string MouseHaptics = "Medium";
            public double FlickSensitivity = 1;
        }

        public string StatusMessage = "Connected";
        public bool BridgeEnabled = true;
        public bool AutoPauseWhenSteam = true;
        public string ActiveProfileId = "";
        public string ActiveProfileName = "";
        public string CurrentGameExe = "";
        public string CurrentGameName = "";
        public string ActiveProfileSource = "";
        public bool DependencyError;
        public bool ViiperOk;
        public string ViiperDetail = "";
        public bool ControllerConnected;
        public string ControllerModel = "";
        public string SelectedDeviceKey = "";
        public bool SelectedHasProfileOverride;
        public bool HasGameIcon;
        public string GameIconToken = "";
        public bool GameBarOverrideActive;
        public string ActiveLayoutId = "";
        public string ActiveLayoutName = "Custom";
        public string LeftTrackpadMode = "Off";
        public string RightTrackpadMode = "Off";
        public string GyroMode = "Off";
        public string GyroButtonMode = "HoldToEnable";
        public string GyroButtonCombine = "Any";
        public bool LayoutsReady;

        public readonly List<ControllerEntry> Controllers = new List<ControllerEntry>();
        public readonly List<string> ProfileNames = new List<string>();
        public readonly List<(string Id, string Name, string Description)> OfficialLayouts = new List<(string, string, string)>();
        public readonly List<JsonObject> Layout = new List<JsonObject>();
        public readonly List<string> GyroButtons = new List<string>();
        public readonly Dictionary<string, string> InputMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, List<(string Activator, List<string> Actions)>> BindingsByInput =
            new Dictionary<string, List<(string, List<string>)>>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, double> SensValues = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, bool> InvertValues = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        public readonly TrackpadSettingsEntry LeftTrackpadSettings = new TrackpadSettingsEntry();
        public readonly TrackpadSettingsEntry RightTrackpadSettings = new TrackpadSettingsEntry();

        static readonly string[] SensKeys =
        {
            "stickSensitivityX", "stickSensitivityY", "trackpadSensitivityX",
            "trackpadSensitivityY", "gyroSensitivity", "gyroSensitivityX", "gyroSensitivityY", "gyroDotsPer360",
            "stickDeadzone", "trackpadDeadzone", "triggerDeadzone"
        };

        static readonly string[] InvertKeys =
        {
            "invertStickX", "invertStickY", "invertTrackpadX",
            "invertTrackpadY", "invertGyroX", "invertGyroY"
        };

        static readonly Dictionary<string, double> DefaultSensValues = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["stickSensitivityX"] = 1, ["stickSensitivityY"] = 1,
            ["trackpadSensitivityX"] = 1, ["trackpadSensitivityY"] = 1,
            ["gyroSensitivity"] = 1,
            ["gyroSensitivityX"] = 1, ["gyroSensitivityY"] = 1,
            ["gyroDotsPer360"] = 6545,
            ["stickDeadzone"] = 0.08, ["trackpadDeadzone"] = 0.02, ["triggerDeadzone"] = 0.05,
        };

        public static bool TryParse(JsonObject state, out HostStateDto dto)
        {
            dto = null;
            if (state == null)
                return false;

            try
            {
                dto = new HostStateDto();
                dto.StatusMessage = state.GetNamedString("statusMessage", "Connected");
                dto.BridgeEnabled = state.GetNamedBoolean("bridgeEnabled", true);
                dto.AutoPauseWhenSteam = state.GetNamedBoolean("autoPauseWhenSteam", true);
                dto.ActiveProfileId = state.GetNamedString("activeProfileId", "");
                dto.ActiveProfileName = state.GetNamedString("activeProfileName", "");
                dto.CurrentGameExe = state.GetNamedString("currentGameExe", "");
                dto.CurrentGameName = state.GetNamedString("currentGameName", "");
                if (string.IsNullOrWhiteSpace(dto.CurrentGameName))
                    dto.CurrentGameName = dto.CurrentGameExe;
                dto.ActiveProfileSource = state.GetNamedString("activeProfileSource", "");
                dto.DependencyError = state.GetNamedBoolean("dependencyError", false);
                dto.ViiperOk = state.GetNamedBoolean("viiperOk", false);
                dto.ViiperDetail = state.GetNamedString("viiperDetail", "");
                dto.ControllerConnected = state.GetNamedBoolean("controllerConnected", false);
                dto.ControllerModel = state.GetNamedString("controllerModel", "");
                dto.SelectedDeviceKey = state.GetNamedString("selectedDeviceKey", "");
                dto.SelectedHasProfileOverride = state.GetNamedBoolean("selectedHasProfileOverride", false);
                dto.HasGameIcon = state.GetNamedBoolean("hasGameIcon", false);
                dto.GameIconToken = state.GetNamedString("gameIconToken", "");
                dto.GameBarOverrideActive = state.GetNamedBoolean("gameBarOverrideActive", false);
                dto.ActiveLayoutId = state.GetNamedString("activeLayoutId", "");
                dto.ActiveLayoutName = state.GetNamedString("activeLayoutName", "Custom");

                ParseControllers(state, dto);
                ParseProfiles(state, dto);
                ParseOfficialLayouts(state, dto);
                ParseLayout(state, dto);
                ParseModes(state, dto);
                ParseSensitivity(state, dto);
                ParseInvert(state, dto);
                ParseGyroButtons(state, dto);
                ParseTrackpadSettings(state, "leftTrackpadSettings", dto.LeftTrackpadSettings);
                ParseTrackpadSettings(state, "rightTrackpadSettings", dto.RightTrackpadSettings);
                ParseInputMap(state, dto);
                ParseBindings(state, dto);
                return true;
            }
            catch
            {
                dto = null;
                return false;
            }
        }

        static void ParseControllers(JsonObject state, HostStateDto dto)
        {
            dto.Controllers.Clear();
            if (!state.ContainsKey("controllers"))
                return;

            var arr = state.GetNamedArray("controllers");
            for (var i = 0; i < arr.Count; i++)
            {
                var item = arr.GetObjectAt((uint)i);
                dto.Controllers.Add(new ControllerEntry
                {
                    DeviceKey = item.GetNamedString("deviceKey", ""),
                    Model = item.GetNamedString("model", ""),
                    DisplayName = item.GetNamedString("displayName", ""),
                    ProfileId = item.GetNamedString("profileId", ""),
                    Order = (int)item.GetNamedNumber("order", i),
                    Connected = item.GetNamedBoolean("connected", true),
                    HasProfileOverride = item.GetNamedBoolean("hasProfileOverride", false),
                    RumbleEnabled = item.GetNamedBoolean("rumbleEnabled", true)
                });
            }
        }

        static void ParseProfiles(JsonObject state, HostStateDto dto)
        {
            dto.ProfileNames.Clear();
            if (!state.ContainsKey("profiles"))
                return;

            foreach (var item in state.GetNamedArray("profiles"))
                dto.ProfileNames.Add(item.GetObject().GetNamedString("name"));
        }

        static void ParseOfficialLayouts(JsonObject state, HostStateDto dto)
        {
            dto.OfficialLayouts.Clear();
            if (!state.ContainsKey("officialLayouts"))
                return;

            foreach (var item in state.GetNamedArray("officialLayouts"))
            {
                var obj = item.GetObject();
                dto.OfficialLayouts.Add((
                    obj.GetNamedString("id"),
                    obj.GetNamedString("name"),
                    obj.GetNamedString("description")));
            }
            dto.LayoutsReady = dto.OfficialLayouts.Count > 0;
        }

        static void ParseLayout(JsonObject state, HostStateDto dto)
        {
            dto.Layout.Clear();
            if (!state.ContainsKey("layout"))
                return;

            foreach (var item in state.GetNamedArray("layout"))
                dto.Layout.Add(item.GetObject());
        }

        static void ParseModes(JsonObject state, HostStateDto dto)
        {
            dto.LeftTrackpadMode = state.GetNamedString("leftTrackpad", "Off");
            dto.RightTrackpadMode = state.GetNamedString("rightTrackpad", "Off");
            dto.GyroMode = state.GetNamedString("gyro", "Off");
        }

        static void ParseSensitivity(JsonObject state, HostStateDto dto)
        {
            dto.SensValues.Clear();
            foreach (var key in SensKeys)
            {
                var fallback = DefaultSensValues.TryGetValue(key, out var defaultValue) ? defaultValue : 1.0;
                dto.SensValues[key] = state.GetNamedNumber(key, fallback);
            }
        }

        static void ParseInvert(JsonObject state, HostStateDto dto)
        {
            dto.InvertValues.Clear();
            foreach (var key in InvertKeys)
                dto.InvertValues[key] = state.GetNamedBoolean(key, false);
        }

        static void ParseGyroButtons(JsonObject state, HostStateDto dto)
        {
            dto.GyroButtons.Clear();
            dto.GyroButtonMode = state.GetNamedString("gyroButtonMode", "HoldToEnable");
            dto.GyroButtonCombine = state.GetNamedString("gyroButtonCombine", "Any");
            if (!state.ContainsKey("gyroButtons"))
                return;

            foreach (var item in state.GetNamedArray("gyroButtons"))
                dto.GyroButtons.Add(item.GetString());
        }

        static void ParseTrackpadSettings(JsonObject state, string key, TrackpadSettingsEntry target)
        {
            if (!state.ContainsKey(key))
                return;

            var obj = state.GetNamedObject(key);
            target.TrackballMode = obj.GetNamedBoolean("trackballMode", false);
            target.TrackballFriction = obj.GetNamedString("trackballFriction", "High");
            target.VerticalFrictionScale = obj.GetNamedNumber("verticalFrictionScale", 1);
            target.Smoothing = obj.GetNamedNumber("smoothing", 20);
            target.RotationDegrees = obj.GetNamedNumber("rotationDegrees", 0);
            target.MouseHaptics = obj.GetNamedString("mouseHaptics", "Medium");
            target.FlickSensitivity = obj.GetNamedNumber("flickSensitivity", 1);
        }

        static void ParseInputMap(JsonObject state, HostStateDto dto)
        {
            dto.InputMap.Clear();
            if (!state.ContainsKey("inputMap"))
                return;

            var map = state.GetNamedObject("inputMap");
            foreach (var mapKey in map.Keys)
                dto.InputMap[mapKey] = map.GetNamedString(mapKey);
        }

        static void ParseBindings(JsonObject state, HostStateDto dto)
        {
            dto.BindingsByInput.Clear();
            if (!state.ContainsKey("bindings"))
                return;

            var root = state.GetNamedObject("bindings");
            foreach (var key in root.Keys)
            {
                var arr = root.GetNamedArray(key);
                var groups = new List<(string Activator, List<string> Actions)>();
                foreach (var item in arr)
                {
                    var obj = item.GetObject();
                    var activator = obj.GetNamedString("activator", "Regular");
                    var actions = new List<string>();
                    if (obj.ContainsKey("actions"))
                    {
                        foreach (var actionItem in obj.GetNamedArray("actions"))
                        {
                            var actionObj = actionItem.GetObject();
                            var kind = actionObj.GetNamedString("kind", "None");
                            if (string.Equals(kind, "None", StringComparison.OrdinalIgnoreCase))
                                continue;
                            var display = actionObj.GetNamedString("display", "");
                            if (!string.IsNullOrEmpty(display))
                                actions.Add(display);
                        }
                    }
                    if (actions.Count > 0)
                        groups.Add((activator, actions));
                }
                if (groups.Count > 0)
                    dto.BindingsByInput[key] = groups;
            }
        }

        public string LayoutCacheKey =>
            Layout.Count + ":" + string.Join(",", Layout.Select(obj => obj.GetNamedString("inputId")));

        public string InputMapCacheKey =>
            string.Join("|",
                InputMap.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(kv => kv.Key + "=" + kv.Value));
    }
}
