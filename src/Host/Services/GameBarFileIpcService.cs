using System.Text;
using System.Text.Json;
using SteamControllerBridge.Shared;

namespace SteamControllerBridge.Host.Services;

/// <summary>
/// File-based IPC for the Game Bar UWP widget (package LocalState).
/// Watches widget-request.txt and publishes widget-state.json.
/// </summary>
public sealed class GameBarFileIpcService : IDisposable
{
    const string PackageNamePrefix = "SteamControllerBridge.GameBar_";
    const string RequestFile = "widget-request.txt";
    const string ResponseFile = "widget-response.txt";
    const string StateFile = "widget-state.json";

    const string HeartbeatFile = "widget-heartbeat.txt";
    const int HeartbeatFreshMs = 2000;

    readonly ProfileService _profiles;
    readonly BridgeService _bridge;
    readonly System.Threading.Timer _timer;
    string? _localStatePath;
    string? _lastRequestId;
    byte[]? _lastStateBytes;
    bool _dirty = true;

    public GameBarFileIpcService(ProfileService profiles, BridgeService bridge)
    {
        _profiles = profiles;
        _bridge = bridge;
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
            Dispatch(command, payload);
            status = "OK";
            responsePayload = "";
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

    void Dispatch(string command, string payload)
    {
        switch (command)
        {
            case "setBridgeEnabled":
                _bridge.SetEnabled(payload.Equals("true", StringComparison.OrdinalIgnoreCase) || payload == "1");
                break;

            case "setAutoPauseWhenSteam":
                _profiles.AutoPauseWhenSteamRunning =
                    payload.Equals("true", StringComparison.OrdinalIgnoreCase) || payload == "1";
                break;

            case "setActiveProfileByName":
            {
                var profile = _profiles.GetProfiles()
                    .FirstOrDefault(p => p.Name.Equals(payload, StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidOperationException("Unknown profile");
                _bridge.SetActiveProfileManual(profile.Id);
                break;
            }

            case "remapButton":
            {
                // legacy: profileId \t Physical \t Xbox
                var bits = payload.Split('\t');
                if (bits.Length < 3) throw new ArgumentException("Invalid remap payload");
                if (!Enum.TryParse<PhysicalInput>(bits[1], true, out var phys))
                    throw new ArgumentException("Invalid physical");
                if (phys == PhysicalInput.Steam)
                    throw new InvalidOperationException("Steam/Guide is locked to Xbox Guide.");
                if (!Enum.TryParse<XboxOutput>(bits[2], true, out var xbox))
                    throw new ArgumentException("Invalid xbox");
                _profiles.Remap(bits[0], phys, xbox);
                break;
            }

            case "remapAction":
            {
                // profileId \t inputId \t kind \t value [\t modifiers]
                // kind: none|xbox|key|mouse
                // value: Xbox name | VK int | Left/Right/Middle
                var bits = payload.Split('\t');
                if (bits.Length < 3) throw new ArgumentException("Invalid remapAction payload");
                if (bits[1].Equals("Steam", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Steam/Guide is locked to Xbox Guide.");
                var action = ParseAction(bits);
                _profiles.RemapAction(bits[0], bits[1], action);
                break;
            }

            case "bindToCurrentGame":
                _bridge.BindActiveProfileToCurrentGame();
                break;

            case "setTrackpadMode":
            {
                // profileId \t left|right \t mode
                var bits = payload.Split('\t');
                if (bits.Length < 3) throw new ArgumentException("Invalid setTrackpadMode payload");
                if (!Enum.TryParse<TrackpadMode>(bits[2], true, out var mode))
                    throw new ArgumentException("Invalid trackpad mode");
                _profiles.SetTrackpad(bits[0], bits[1].Equals("left", StringComparison.OrdinalIgnoreCase), mode);
                break;
            }

            case "setGyroMode":
            {
                // profileId \t mode
                var bits = payload.Split('\t');
                if (bits.Length < 2) throw new ArgumentException("Invalid setGyroMode payload");
                if (!Enum.TryParse<GyroMode>(bits[1], true, out var mode))
                    throw new ArgumentException("Invalid gyro mode");
                _profiles.SetGyro(bits[0], mode);
                break;
            }

            case "createFromLayout":
            {
                // layoutId [\t name]
                var bits = payload.Split('\t');
                if (bits.Length < 1 || string.IsNullOrWhiteSpace(bits[0]))
                    throw new ArgumentException("layoutId required");
                var created = _profiles.CreateFromLayout(bits[0], bits.Length > 1 ? bits[1] : null);
                _bridge.SetActiveProfileManual(created.Id);
                break;
            }

            case "duplicateProfile":
            {
                // profileId [\t name]
                var bits = payload.Split('\t');
                if (bits.Length < 1) throw new ArgumentException("profileId required");
                var created = _profiles.Duplicate(bits[0], bits.Length > 1 ? bits[1] : null);
                _bridge.SetActiveProfileManual(created.Id);
                break;
            }

            case "renameProfile":
            {
                // profileId \t name
                var bits = payload.Split('\t');
                if (bits.Length < 2) throw new ArgumentException("Invalid renameProfile payload");
                _profiles.Rename(bits[0], bits[1]);
                break;
            }

            case "deleteProfile":
            {
                _profiles.Delete(payload.Trim());
                break;
            }

            case "ensureOfficialLayouts":
                _profiles.EnsureOfficialLayouts();
                break;

            default:
                throw new InvalidOperationException("Unknown command: " + command);
        }
    }

    static OutputAction ParseAction(string[] bits)
    {
        var kind = bits.Length > 2 ? bits[2] : "none";
        var value = bits.Length > 3 ? bits[3] : "";
        var mods = bits.Length > 4 && int.TryParse(bits[4], out var m) ? (KeyModifiers)m : KeyModifiers.None;

        return kind.ToLowerInvariant() switch
        {
            "xbox" when Enum.TryParse<XboxOutput>(value, true, out var xbox) => OutputAction.FromXbox(xbox),
            "key" when int.TryParse(value, out var vk) => OutputAction.FromKey(vk, mods),
            "mouse" when Enum.TryParse<MouseButtonOutput>(value, true, out var mb) => OutputAction.FromMouse(mb),
            _ => OutputAction.None()
        };
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
        // Always publish the user's saved profile for the widget UI — never the Game Bar runtime override.
        var active = _profiles.GetProfiles().FirstOrDefault(p => p.Id == status.ActiveProfileId)
                     ?? _profiles.ActiveProfile;
        var profiles = _profiles.GetProfiles();
        var caps = _bridge.Drivers.GetCapabilities(status.ActiveDriverId);

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
            writer.WriteString("activeProfileSource", status.ActiveProfileSource);
            writer.WriteString("currentGameExe", status.CurrentGameExe ?? "");
            writer.WriteString("activeDriverId", status.ActiveDriverId ?? "");
            writer.WriteString("activeDriverName", status.ActiveDriverName ?? "");

            var viiper = status.Dependencies.FirstOrDefault(d => d.Id == "viiper");
            writer.WriteBoolean("viiperOk", viiper?.Ok ?? status.ViiperConnected);
            writer.WriteString("viiperDetail", viiper?.Detail ?? "");
            writer.WriteBoolean("dependencyError", viiper is { Ok: false });
            writer.WriteBoolean("controllerConnected", status.ControllerConnected);
            writer.WriteString("runState", status.State.ToString());
            writer.WriteBoolean("gameBarOverrideActive", status.GameBarOverrideActive);
            writer.WriteString("leftTrackpad", active.LeftTrackpad.ToString());
            writer.WriteString("rightTrackpad", active.RightTrackpad.ToString());
            writer.WriteString("gyro", active.Gyro.ToString());

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
            foreach (var (key, action) in active.InputMap)
                writer.WriteString(key, action.ToDisplayString());
            writer.WriteEndObject();

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
                var mapped = active.GetAction(spot.InputId).ToDisplayString();
                writer.WriteString("mapped", mapped);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            // legacy paddle map for older widgets
            writer.WriteStartObject("paddleMap");
            foreach (var paddle in new[] { "L4", "L5", "R4", "R5" })
                writer.WriteString(paddle, active.GetAction(paddle).ToDisplayString());
            writer.WriteEndObject();

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

    public void Dispose() => _timer.Dispose();
}
