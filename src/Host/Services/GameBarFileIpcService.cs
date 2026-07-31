using System.Globalization;
using System.Text;
using System.Text.Json;
using MistMapper.Shared;

namespace MistMapper.Host.Services;

/// <summary>
/// File-based IPC for the Game Bar UWP widget (package LocalState).
/// Watches widget-request.txt and publishes widget-state.json.
/// </summary>
public sealed class GameBarFileIpcService : IDisposable
{
    static readonly JsonSerializerOptions CaseInsensitiveJson = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };
    const string PackageNamePrefix = "MistMapper.GameBar_";
    const string RequestFile = "widget-request.txt";
    const string ResponseFile = "widget-response.txt";
    const string StateFile = "widget-state.json";

    const string HeartbeatFile = "widget-heartbeat.txt";
    const int HeartbeatFreshMs = 2000;

    readonly ProfileService _profiles;
    readonly BridgeService _bridge;
    readonly HostCommandService _commands;
    readonly System.Threading.Timer _timer;
    string? _localStatePath;
    string? _lastRequestId;
    byte[]? _lastStateBytes;
    bool _dirty = true;
    string? _lastIconGamePath;
    string _lastIconToken = "";

    public GameBarFileIpcService(ProfileService profiles, BridgeService bridge, HostCommandService commands)
    {
        _profiles = profiles;
        _bridge = bridge;
        _commands = commands;
        // Do not subscribe to StatusChanged — bridging fires it every frame and races the UWP reader.
        // The timer publishes pressed/status; profile edits force an immediate write.
        _timer = new System.Threading.Timer(_ => Tick(), null, 400, 200);
        _profiles.Changed += () =>
        {
            _dirty = true;
            WriteStateSafe();
        };
    }

    void Tick()
    {
        try
        {
            EnsureLocalStatePath();
            if (_localStatePath is null) return;
            UpdateGameBarPresence();
            HandleRequest();
            WriteState();
        }
        catch
        {
            // ignore transient IO
        }
    }

    void UpdateGameBarPresence()
    {
        if (_localStatePath is null) return;
        var path = Path.Combine(_localStatePath, HeartbeatFile);
        var open = false;
        try
        {
            if (File.Exists(path))
            {
                var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(path);
                open = age.TotalMilliseconds >= 0 && age.TotalMilliseconds < HeartbeatFreshMs;
            }
        }
        catch { /* ignore */ }
        _bridge.SetGameBarWidgetOpen(open);
    }

    void EnsureLocalStatePath()
    {
        if (_localStatePath is not null && Directory.Exists(_localStatePath))
            return;

        var packages = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Packages");
        if (!Directory.Exists(packages)) return;

        var match = Directory.GetDirectories(packages, PackageNamePrefix + "*")
            .OrderByDescending(Directory.GetLastWriteTimeUtc)
            .FirstOrDefault();
        if (match is null) return;

        var localState = Path.Combine(match, "LocalState");
        Directory.CreateDirectory(localState);
        _localStatePath = localState;
    }

    void HandleRequest()
    {
        if (_localStatePath is null) return;
        var requestPath = Path.Combine(_localStatePath, RequestFile);
        if (!File.Exists(requestPath)) return;

        string text;
        try { text = File.ReadAllText(requestPath); }
        catch { return; }

        var parts = text.Replace("\r", "").Split('\n');
        if (parts.Length < 2) return;
        var id = parts[0].Trim();
        if (string.IsNullOrEmpty(id) || id == _lastRequestId) return;

        var command = parts[1].Trim();
        var payload = parts.Length >= 3 ? parts[2] : "";

        string status;
        string responsePayload;
        try
        {
            responsePayload = Dispatch(command, payload) ?? "";
            status = "OK";
        }
        catch (Exception ex)
        {
            status = "ERR";
            responsePayload = ex.Message.Replace('\n', ' ');
        }

        _lastRequestId = id;
        var responsePath = Path.Combine(_localStatePath, ResponseFile);
        File.WriteAllText(responsePath, id + "\n" + status + "\n" + responsePayload + "\n", Encoding.UTF8);
        try { File.Delete(requestPath); } catch { /* ignore */ }
        WriteState();
    }

    string? Dispatch(string command, string payload)
    {
        switch (command)
        {
            case "setBridgeEnabled":
                _bridge.SetEnabled(payload.Equals("true", StringComparison.OrdinalIgnoreCase) || payload == "1");
                return null;

            case "setAutoPauseWhenSteam":
                _profiles.AutoPauseWhenSteamRunning =
                    payload.Equals("true", StringComparison.OrdinalIgnoreCase) || payload == "1";
                return null;

            case "setActiveProfileByName":
            {
                var profile = _profiles.GetUserProfiles()
                    .FirstOrDefault(p => p.Name.Equals(payload, StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidOperationException("Unknown profile");
                _bridge.SetActiveProfileManual(profile.Id);
                return null;
            }

            case "remapButton":
            {
                // legacy: profileId \t Physical \t Xbox
                var bits = payload.Split('\t');
                if (bits.Length < 3) throw new ArgumentException("Invalid remap payload");
                if (!Enum.TryParse<PhysicalInput>(bits[1], true, out var phys))
                    throw new ArgumentException("Invalid physical");
                if (!Enum.TryParse<XboxOutput>(bits[2], true, out var xbox))
                    throw new ArgumentException("Invalid xbox");
                _commands.RemapButton(bits[0], phys, xbox);
                return null;
            }

            case "remapAction":
            {
                // profileId \t inputId \t kind \t value [\t modifiers]
                // kind: none|xbox|key|mouse
                // value: Xbox name | VK int | Left/Right/Middle/ScrollUp/ScrollDown
                var bits = payload.Split('\t');
                if (bits.Length < 3) throw new ArgumentException("Invalid remapAction payload");
                var action = HostCommandService.ParseAction(bits);
                _commands.RemapAction(bits[0], bits[1], action);
                return null;
            }

            case "setBinding":
            {
                // profileId \t inputId \t activator \t slot \t kind \t value [\t modifiers]
                var bits = payload.Split('\t');
                if (bits.Length < 5) throw new ArgumentException("Invalid setBinding payload");
                if (!Enum.TryParse<ActivatorType>(bits[2], true, out var activator))
                    throw new ArgumentException("Invalid activator");
                if (!int.TryParse(bits[3], out var slot))
                    throw new ArgumentException("Invalid slot");
                // Rebase ParseAction: kind at [4], value at [5], mods at [6]
                var actionBits = new[] { bits[0], bits[1], bits.Length > 4 ? bits[4] : "none", bits.Length > 5 ? bits[5] : "", bits.Length > 6 ? bits[6] : "0" };
                var action = HostCommandService.ParseAction(actionBits);
                _commands.SetBinding(bits[0], bits[1], activator, slot, action);
                return null;
            }

            case "bindToCurrentGame":
                _bridge.BindActiveProfileToCurrentGame();
                return null;

            case "setTrackpadMode":
            {
                // profileId \t left|right \t mode
                var bits = payload.Split('\t');
                if (bits.Length < 3) throw new ArgumentException("Invalid setTrackpadMode payload");
                if (!Enum.TryParse<TrackpadMode>(bits[2], true, out var mode))
                    throw new ArgumentException("Invalid trackpad mode");
                _commands.SetTrackpadMode(bits[0], bits[1].Equals("left", StringComparison.OrdinalIgnoreCase), mode);
                return null;
            }

            case "setGyroMode":
            {
                // profileId \t mode
                var bits = payload.Split('\t');
                if (bits.Length < 2) throw new ArgumentException("Invalid setGyroMode payload");
                if (!Enum.TryParse<GyroMode>(bits[1], true, out var mode))
                    throw new ArgumentException("Invalid gyro mode");
                _commands.SetGyroMode(bits[0], mode);
                return null;
            }

            case "createFromLayout":
            {
                // layoutId [\t name]
                var bits = payload.Split('\t');
                if (bits.Length < 1 || string.IsNullOrWhiteSpace(bits[0]))
                    throw new ArgumentException("layoutId required");
                var created = _profiles.CreateFromLayout(bits[0], bits.Length > 1 ? bits[1] : null);
                _bridge.SetActiveProfileManual(created.Id);
                return null;
            }

            case "applyLayout":
            {
                // profileId \t layoutId
                var bits = payload.Split('\t');
                if (bits.Length < 2)
                    throw new ArgumentException("profileId and layoutId required");
                bits[0] = _bridge.ResolveRemapTargetProfileId(bits[0]);
                _profiles.ApplyLayout(bits[0], bits[1]);
                return null;
            }

            case "saveAsProfile":
            {
                // profileId \t name
                var bits = payload.Split('\t');
                if (bits.Length < 2 || string.IsNullOrWhiteSpace(bits[1]))
                    throw new ArgumentException("profileId and name required");
                var created = _profiles.SaveAsProfile(bits[0], bits[1]);
                var exe = _bridge.Status.CurrentGameExe;
                if (!string.IsNullOrWhiteSpace(exe) && _profiles.FindBindingForGame(exe, _bridge.Status.CurrentGamePath) is null)
                    _profiles.BindToGame(created.Id, exe, null, _bridge.Status.CurrentGameName);
                _bridge.SetActiveProfileManual(created.Id);
                return null;
            }

            case "previewLayout":
            {
                // layoutId → JSON { "inputMap": { ... } }
                if (string.IsNullOrWhiteSpace(payload))
                    throw new ArgumentException("layoutId required");
                var map = ProfileService.PreviewLayoutMap(payload.Trim());
                using var stream = new MemoryStream();
                using (var writer = new Utf8JsonWriter(stream))
                {
                    writer.WriteStartObject();
                    writer.WriteString("layoutId", payload.Trim());
                    writer.WriteString("layoutName", ProfileService.LayoutDisplayName(payload.Trim()));
                    writer.WriteStartObject("inputMap");
                    foreach (var (key, value) in map)
                        writer.WriteString(key, value);
                    writer.WriteEndObject();
                    writer.WriteEndObject();
                }
                return Encoding.UTF8.GetString(stream.ToArray());
            }

            case "duplicateProfile":
            {
                // profileId [\t name]
                var bits = payload.Split('\t');
                if (bits.Length < 1) throw new ArgumentException("profileId required");
                var created = _profiles.Duplicate(bits[0], bits.Length > 1 ? bits[1] : null);
                _bridge.SetActiveProfileManual(created.Id);
                return null;
            }

            case "renameProfile":
            {
                // profileId \t name
                var bits = payload.Split('\t');
                if (bits.Length < 2) throw new ArgumentException("Invalid renameProfile payload");
                _profiles.Rename(bits[0], bits[1]);
                return null;
            }

            case "deleteProfile":
            {
                _profiles.Delete(payload.Trim());
                return null;
            }

            case "setSensitivity":
            {
                // JSON payload matching SensitivityPayload
                var p = JsonSerializer.Deserialize<SensitivityPayload>(payload, CaseInsensitiveJson)
                    ?? throw new ArgumentException("Invalid sensitivity payload");
                _commands.SetSensitivity(p.ProfileId, p);
                return null;
            }

            case "ensureOfficialLayouts":
                _profiles.EnsureOfficialLayouts();
                return null;

            case "setSelectedController":
                _bridge.SetSelectedController(payload.Trim());
                return null;

            case "setControllerSlotOrder":
            {
                // JSON array of { deviceKey, ... } in desired order
                var slots = JsonSerializer.Deserialize<List<ControllerSlot>>(payload, CaseInsensitiveJson)
                    ?? throw new ArgumentException("Invalid setControllerSlotOrder payload");
                _profiles.SetControllerSlotOrder(slots);
                return null;
            }

            case "setControllerSlotProfile":
            {
                // deviceKey \t profileId| (empty clears override)
                var bits = payload.Split('\t');
                if (bits.Length < 1 || string.IsNullOrWhiteSpace(bits[0]))
                    throw new ArgumentException("deviceKey required");
                var profileId = bits.Length > 1 && !string.IsNullOrWhiteSpace(bits[1]) ? bits[1] : null;
                _profiles.SetControllerSlotProfile(bits[0].Trim(), profileId);
                return null;
            }

            case "makeControllerProfileUnique":
            {
                // deviceKey [\t sourceProfileId]
                var bits = payload.Split('\t');
                if (bits.Length < 1 || string.IsNullOrWhiteSpace(bits[0]))
                    throw new ArgumentException("deviceKey required");
                var source = bits.Length > 1 && !string.IsNullOrWhiteSpace(bits[1]) ? bits[1] : null;
                return _bridge.MakeControllerProfileUnique(bits[0].Trim(), source);
            }

            case "identifyController":
            {
                var key = payload.Trim();
                if (string.IsNullOrWhiteSpace(key))
                    throw new ArgumentException("deviceKey required");
                _bridge.IdentifyControllerAsync(key).GetAwaiter().GetResult();
                return null;
            }

            case "renameController":
            {
                // deviceKey \t displayName (empty name clears custom label)
                var bits = payload.Split('\t');
                if (bits.Length < 1 || string.IsNullOrWhiteSpace(bits[0]))
                    throw new ArgumentException("deviceKey required");
                var name = bits.Length > 1 ? bits[1] : "";
                _bridge.RenameController(bits[0].Trim(), string.IsNullOrWhiteSpace(name) ? null : name);
                return null;
            }

            case "setControllerRumble":
            {
                // deviceKey \t true|false
                var bits = payload.Split('\t');
                if (bits.Length < 2 || string.IsNullOrWhiteSpace(bits[0]))
                    throw new ArgumentException("Invalid setControllerRumble payload");
                var on = bits[1].Equals("true", StringComparison.OrdinalIgnoreCase) || bits[1] == "1";
                _bridge.SetControllerRumbleEnabled(bits[0].Trim(), on);
                return null;
            }

            default:
                throw new InvalidOperationException("Unknown command: " + command);
        }
    }

    void WriteStateSafe()
    {
        try
        {
            EnsureLocalStatePath();
            WriteState();
        }
        catch { /* ignore */ }
    }

    void WriteState()
    {
        if (_localStatePath is null) return;
        var status = _bridge.Status;
        // Selected pad's resolved profile for the widget UI.
        var active = _bridge.GetSelectedResolvedProfile();
        var profiles = _profiles.GetUserProfiles();
        var caps = _bridge.GetActiveCapabilities();

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("statusMessage", $"{status.State}: {status.Message}");
            writer.WriteBoolean("bridgeEnabled", status.BridgeEnabled);
            writer.WriteBoolean("autoPauseWhenSteam", status.AutoPauseWhenSteamRunning);
            writer.WriteBoolean("steamRunning", status.SteamRunning);
            writer.WriteString("activeProfileId", active.Id);
            writer.WriteString("activeProfileName", active.Name);
            writer.WriteString("activeLayoutId", active.LayoutId ?? "");
            writer.WriteString("activeLayoutName", ProfileService.LayoutDisplayName(active.LayoutId));
            writer.WriteString("activeProfileSource", status.ActiveProfileSource);
            writer.WriteString("currentGameExe", status.CurrentGameExe ?? "");
            writer.WriteString("currentGameName", status.CurrentGameName ?? "");
            var (hasIcon, iconToken) = EnsureGameIcon(status.CurrentGamePath);
            writer.WriteBoolean("hasGameIcon", hasIcon);
            writer.WriteString("gameIconFile", hasIcon ? "current-game-icon.png" : "");
            writer.WriteString("gameIconToken", iconToken);
            writer.WriteString("activeDriverId", status.ActiveDriverId ?? "");
            writer.WriteString("activeDriverName", status.ActiveDriverName ?? "");

            var viiper = status.Dependencies.FirstOrDefault(d => d.Id == "viiper");
            writer.WriteBoolean("viiperOk", viiper?.Ok ?? status.ViiperConnected);
            writer.WriteString("viiperDetail", viiper?.Detail ?? "");
            writer.WriteBoolean("dependencyError", viiper is { Ok: false });
            writer.WriteBoolean("controllerConnected", status.ControllerConnected);
            writer.WriteString("controllerModel", status.ControllerConnected
                ? (string.IsNullOrEmpty(status.ControllerModel) ? "sc2" : status.ControllerModel)
                : "");
            writer.WriteString("selectedDeviceKey", status.SelectedDeviceKey ?? "");
            writer.WriteBoolean("selectedHasProfileOverride", _bridge.SelectedPadHasProfileOverride());
            writer.WriteString("runState", status.State.ToString());
            writer.WriteBoolean("gameBarOverrideActive", status.GameBarOverrideActive);
            writer.WriteString("leftTrackpad", active.LeftTrackpad.ToString());
            writer.WriteString("rightTrackpad", active.RightTrackpad.ToString());
            writer.WriteString("gyro", active.Gyro.ToString());
            writer.WriteNumber("gyroSensitivity", active.GyroSensitivity);
            writer.WriteNumber("gyroDotsPer360", active.GyroDotsPer360);
            writer.WriteString("gyroButtonMode", active.GyroButtonMode.ToString());
            writer.WriteString("gyroButtonCombine", active.GyroButtonCombine.ToString());
            writer.WriteStartArray("gyroButtons");
            foreach (var b in active.GyroButtons)
                writer.WriteStringValue(b);
            writer.WriteEndArray();

            writer.WriteNumber("stickSensitivityX", active.StickSensitivityX);
            writer.WriteNumber("stickSensitivityY", active.StickSensitivityY);
            writer.WriteNumber("trackpadSensitivityX", active.TrackpadSensitivityX);
            writer.WriteNumber("trackpadSensitivityY", active.TrackpadSensitivityY);
            writer.WriteNumber("gyroSensitivityX", active.GyroSensitivityX);
            writer.WriteNumber("gyroSensitivityY", active.GyroSensitivityY);
            writer.WriteNumber("stickDeadzone", active.StickDeadzone);
            writer.WriteNumber("trackpadDeadzone", active.TrackpadDeadzone);
            writer.WriteNumber("triggerDeadzone", active.TriggerDeadzone);
            writer.WriteBoolean("invertStickX", active.InvertStickX);
            writer.WriteBoolean("invertStickY", active.InvertStickY);
            writer.WriteBoolean("invertTrackpadX", active.InvertTrackpadX);
            writer.WriteBoolean("invertTrackpadY", active.InvertTrackpadY);
            writer.WriteBoolean("invertGyroX", active.InvertGyroX);
            writer.WriteBoolean("invertGyroY", active.InvertGyroY);

            WriteTrackpadSettings(writer, "leftTrackpadSettings", active.LeftTrackpadSettings);
            WriteTrackpadSettings(writer, "rightTrackpadSettings", active.RightTrackpadSettings);

            writer.WriteStartArray("controllers");
            foreach (var c in status.Controllers.OrderBy(x => x.Order))
            {
                writer.WriteStartObject();
                writer.WriteString("deviceKey", c.DeviceKey);
                writer.WriteString("model", c.Model);
                writer.WriteString("displayName", c.DisplayName);
                writer.WriteNumber("order", c.Order);
                writer.WriteBoolean("enabled", c.Enabled);
                writer.WriteBoolean("connected", c.Connected);
                writer.WriteBoolean("rumbleEnabled", c.RumbleEnabled);
                writer.WriteString("profileId", c.ProfileId ?? "");
                writer.WriteString("profileName", c.ProfileName ?? "");
                writer.WriteBoolean("hasProfileOverride", c.HasProfileOverride);
                writer.WriteStartArray("pressed");
                foreach (var id in c.PressedInputs)
                    writer.WriteStringValue(id);
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WriteStartArray("profiles");
            foreach (var p in profiles)
            {
                writer.WriteStartObject();
                writer.WriteString("id", p.Id);
                writer.WriteString("name", p.Name);
                writer.WriteString("layoutId", p.LayoutId ?? "");
                writer.WriteBoolean("isOfficial", p.IsOfficial);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WriteStartArray("officialLayouts");
            foreach (var layout in OfficialLayouts.All)
            {
                writer.WriteStartObject();
                writer.WriteString("id", layout.Id);
                writer.WriteString("name", layout.Name);
                writer.WriteString("description", layout.Description);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WriteStartObject("inputMap");
            foreach (var key in active.Bindings.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
                writer.WriteString(key, active.FormatBindingsDisplay(key));
            writer.WriteEndObject();

            writer.WriteStartObject("bindings");
            foreach (var (key, list) in active.Bindings)
            {
                writer.WriteStartArray(key);
                foreach (var b in list)
                {
                    writer.WriteStartObject();
                    writer.WriteString("activator", b.Activator.ToString());
                    writer.WriteStartArray("actions");
                    foreach (var a in b.Actions)
                    {
                        writer.WriteStartObject();
                        writer.WriteString("kind", a.Kind.ToString());
                        writer.WriteString("display", a.ToDisplayString());
                        if (a.Kind == OutputActionKind.Xbox) writer.WriteString("xbox", a.Xbox.ToString());
                        if (a.Kind == OutputActionKind.Key)
                        {
                            writer.WriteNumber("virtualKey", a.VirtualKey);
                            writer.WriteNumber("modifiers", (int)a.Modifiers);
                        }
                        if (a.Kind == OutputActionKind.MouseButton)
                            writer.WriteString("mouseButton", a.MouseButton.ToString());
                        writer.WriteEndObject();
                    }
                    writer.WriteEndArray();
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
            }
            writer.WriteEndObject();

            writer.WriteNumber("longPressMs", active.LongPressMs);

            writer.WriteStartArray("pressed");
            foreach (var id in status.PressedInputs)
                writer.WriteStringValue(id);
            writer.WriteEndArray();

            writer.WriteStartArray("layout");
            foreach (var spot in caps.Layout)
            {
                writer.WriteStartObject();
                writer.WriteString("inputId", spot.InputId);
                writer.WriteString("label", spot.Label);
                writer.WriteString("shape", spot.Shape);
                writer.WriteNumber("x", spot.X);
                writer.WriteNumber("y", spot.Y);
                writer.WriteNumber("width", spot.Width);
                writer.WriteNumber("height", spot.Height);
                var remappable = spot.Remappable && !MappingLocks.IsLockedGuideInput(spot.InputId);
                writer.WriteBoolean("remappable", remappable);
                // Always serialize the *active profile* mapping (not Game Bar runtime override).
                var mapped = active.FormatBindingsDisplay(spot.InputId);
                writer.WriteString("mapped", mapped);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            // legacy paddle map for older widgets — only paddles this pad actually has
            writer.WriteStartObject("paddleMap");
            foreach (var paddle in new[] { "L4", "L5", "R4", "R5" })
            {
                if (caps.Inputs.All(i => !i.Id.Equals(paddle, StringComparison.OrdinalIgnoreCase)))
                    continue;
                writer.WriteString(paddle, active.FormatBindingsDisplay(paddle));
            }
            writer.WriteEndObject();

            writer.WriteEndObject();
        }

        static void WriteTrackpadSettings(Utf8JsonWriter writer, string name, TrackpadSurfaceSettings s)
        {
            writer.WriteStartObject(name);
            writer.WriteBoolean("trackballMode", s.TrackballMode);
            writer.WriteString("trackballFriction", s.TrackballFriction.ToString());
            writer.WriteNumber("verticalFrictionScale", s.VerticalFrictionScale);
            writer.WriteNumber("smoothing", s.Smoothing);
            writer.WriteNumber("rotationDegrees", s.RotationDegrees);
            writer.WriteString("mouseHaptics", s.MouseHaptics.ToString());
            writer.WriteNumber("flickSensitivity", s.FlickSensitivity);
            writer.WriteEndObject();
        }

        var bytes = stream.ToArray();
        if (!_dirty && _lastStateBytes is not null && bytes.AsSpan().SequenceEqual(_lastStateBytes))
            return;

        var path = Path.Combine(_localStatePath, StateFile);
        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, bytes);
        // Atomic replace avoids empty/partial reads that made the widget flicker "Host not running".
        File.Move(tmp, path, overwrite: true);
        _lastStateBytes = bytes;
        _dirty = false;
    }

    (bool HasIcon, string Token) EnsureGameIcon(string? gamePath)
    {
        if (_localStatePath is null)
            return (false, "");

        var iconPath = Path.Combine(_localStatePath, "current-game-icon.png");
        if (string.IsNullOrWhiteSpace(gamePath) || !File.Exists(gamePath))
        {
            _lastIconGamePath = null;
            _lastIconToken = "";
            try
            {
                if (File.Exists(iconPath))
                    File.Delete(iconPath);
            }
            catch { /* ignore */ }
            return (false, "");
        }

        if (string.Equals(_lastIconGamePath, gamePath, StringComparison.OrdinalIgnoreCase)
            && File.Exists(iconPath)
            && !string.IsNullOrEmpty(_lastIconToken))
            return (true, _lastIconToken);

        if (!GameIconExtractor.TryWritePng(gamePath, iconPath) || !File.Exists(iconPath))
        {
            _lastIconGamePath = null;
            _lastIconToken = "";
            return (false, "");
        }

        _lastIconGamePath = gamePath;
        _lastIconToken = DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture);
        return (true, _lastIconToken);
    }

    public void Dispose() => _timer.Dispose();
}
