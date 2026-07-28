using System.Text.Json;
using System.Text.Json.Serialization;

namespace SteamControllerBridge.Shared;

public static class IpcProtocol
{
    public const string PipeName = "SteamControllerBridge.Ipc";
    public const int ProtocolVersion = 2;

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
}

public sealed class IpcRequest
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Command { get; set; } = "";
    public JsonElement? Payload { get; set; }
}

public sealed class IpcResponse
{
    public string Id { get; set; } = "";
    public bool Ok { get; set; }
    public string? Error { get; set; }
    public JsonElement? Payload { get; set; }
}

public static class IpcCommands
{
    public const string GetStatus = "getStatus";
    public const string GetProfiles = "getProfiles";
    public const string SetActiveProfile = "setActiveProfile";
    public const string UpsertProfile = "upsertProfile";
    public const string DeleteProfile = "deleteProfile";
    public const string RemapButton = "remapButton";
    public const string RemapAction = "remapAction";
    public const string SetBridgeEnabled = "setBridgeEnabled";
    public const string SetAutoPauseWhenSteam = "setAutoPauseWhenSteam";
    public const string SetTrackpadMode = "setTrackpadMode";
    public const string SetGyroMode = "setGyroMode";
    public const string BindProfileToGame = "bindProfileToGame";
    public const string CreateDefaultProfiles = "createDefaultProfiles";
    public const string GetDriverCapabilities = "getDriverCapabilities";
}

public sealed class RemapButtonPayload
{
    public string ProfileId { get; set; } = "";
    public string Physical { get; set; } = "";
    public string Xbox { get; set; } = "";
}

public sealed class RemapActionPayload
{
    public string ProfileId { get; set; } = "";
    public string InputId { get; set; } = "";
    public OutputAction Action { get; set; } = OutputAction.None();
}

public sealed class SetActiveProfilePayload
{
    public string ProfileId { get; set; } = "";
}

public sealed class SetBridgeEnabledPayload
{
    public bool Enabled { get; set; }
}

public sealed class SetAutoPauseWhenSteamPayload
{
    public bool Enabled { get; set; }
}

public sealed class SetTrackpadModePayload
{
    public string ProfileId { get; set; } = "";
    public bool Left { get; set; }
    public string Mode { get; set; } = "Off";
}

public sealed class SetGyroModePayload
{
    public string ProfileId { get; set; } = "";
    public string Mode { get; set; } = "Off";
    public float? Sensitivity { get; set; }
}

public sealed class BindProfileToGamePayload
{
    public string ProfileId { get; set; } = "";
    public string MatchExe { get; set; } = "";
    public string? MatchPathContains { get; set; }
    public string? DisplayName { get; set; }
}

public sealed class ProfilesPayload
{
    public string ActiveProfileId { get; set; } = "";
    public List<ControllerProfile> Profiles { get; set; } = [];
    public List<ProfileBinding> ProfileBindings { get; set; } = [];
}
