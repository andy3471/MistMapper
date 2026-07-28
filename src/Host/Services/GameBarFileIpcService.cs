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

    readonly ProfileService _profiles;
    readonly BridgeService _bridge;
    readonly System.Threading.Timer _timer;
    string? _localStatePath;
    string? _lastRequestId;

    public GameBarFileIpcService(ProfileService profiles, BridgeService bridge)
    {
        _profiles = profiles;
        _bridge = bridge;
        _timer = new System.Threading.Timer(_ => Tick(), null, 500, 400);
        _bridge.StatusChanged += _ => WriteStateSafe();
        _profiles.Changed += WriteStateSafe;
    }

    void Tick()
    {
        try
        {
            EnsureLocalStatePath();
            if (_localStatePath is null) return;
            HandleRequest();
            WriteState();
        }
        catch
        {
            // ignore transient IO
        }
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

            case "setActiveProfileByName":
            {
                var profile = _profiles.GetProfiles()
                    .FirstOrDefault(p => p.Name.Equals(payload, StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidOperationException("Unknown profile");
                _profiles.SetActiveProfile(profile.Id);
                break;
            }

            case "remapButton":
            {
                // profileId \t Physical \t Xbox
                var bits = payload.Split('\t');
                if (bits.Length < 3) throw new ArgumentException("Invalid remap payload");
                if (!Enum.TryParse<PhysicalInput>(bits[1], true, out var phys))
                    throw new ArgumentException("Invalid physical");
                if (!Enum.TryParse<XboxOutput>(bits[2], true, out var xbox))
                    throw new ArgumentException("Invalid xbox");
                _profiles.Remap(bits[0], phys, xbox);
                break;
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
        var active = _profiles.ActiveProfile;
        var profiles = _profiles.GetProfiles();

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("statusMessage", $"{status.State}: {status.Message}");
            writer.WriteBoolean("bridgeEnabled", status.BridgeEnabled);
            writer.WriteString("activeProfileId", active.Id);
            writer.WriteString("activeProfileName", active.Name);
            writer.WriteStartArray("profiles");
            foreach (var p in profiles)
            {
                writer.WriteStartObject();
                writer.WriteString("id", p.Id);
                writer.WriteString("name", p.Name);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteStartObject("paddleMap");
            foreach (var paddle in new[] { PhysicalInput.L4, PhysicalInput.L5, PhysicalInput.R4, PhysicalInput.R5 })
                writer.WriteString(paddle.ToString(), active.MapButton(paddle).ToString());
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        var path = Path.Combine(_localStatePath, StateFile);
        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, stream.ToArray());
        File.Copy(tmp, path, overwrite: true);
        try { File.Delete(tmp); } catch { /* ignore */ }
    }

    public void Dispose() => _timer.Dispose();
}
