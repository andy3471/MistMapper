using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using SteamControllerBridge.Shared;

namespace SteamControllerBridge.Host.Services;

public sealed class IpcServer : IDisposable
{
    readonly ProfileService _profiles;
    readonly BridgeService _bridge;
    readonly CancellationTokenSource _cts = new();
    readonly List<Task> _clients = [];
    Task? _listenTask;

    public IpcServer(ProfileService profiles, BridgeService bridge)
    {
        _profiles = profiles;
        _bridge = bridge;
    }

    public void Start()
    {
        _listenTask = Task.Run(ListenLoopAsync);
    }

    async Task ListenLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            var server = new NamedPipeServerStream(
                IpcProtocol.PipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            try
            {
                await server.WaitForConnectionAsync(_cts.Token);
                var clientTask = HandleClientAsync(server);
                lock (_clients) _clients.Add(clientTask);
                _ = clientTask.ContinueWith(t =>
                {
                    lock (_clients) _clients.Remove(t);
                    server.Dispose();
                });
            }
            catch (OperationCanceledException)
            {
                server.Dispose();
                break;
            }
            catch
            {
                server.Dispose();
                await Task.Delay(200);
            }
        }
    }

    async Task HandleClientAsync(NamedPipeServerStream pipe)
    {
        using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
        using var writer = new StreamWriter(pipe, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

        while (pipe.IsConnected && !_cts.IsCancellationRequested)
        {
            string? line;
            try { line = await reader.ReadLineAsync(); }
            catch { break; }
            if (line is null) break;

            IpcResponse response;
            try
            {
                var request = JsonSerializer.Deserialize<IpcRequest>(line, IpcProtocol.JsonOptions)
                              ?? throw new InvalidOperationException("Invalid request");
                response = Dispatch(request);
            }
            catch (Exception ex)
            {
                response = new IpcResponse { Ok = false, Error = ex.Message };
            }

            var json = JsonSerializer.Serialize(response, IpcProtocol.JsonOptions);
            await writer.WriteLineAsync(json);
        }
    }

    IpcResponse Dispatch(IpcRequest request)
    {
        var response = new IpcResponse { Id = request.Id, Ok = true };
        switch (request.Command)
        {
            case IpcCommands.GetStatus:
                response.Payload = JsonSerializer.SerializeToElement(_bridge.Status, IpcProtocol.JsonOptions);
                break;

            case IpcCommands.GetProfiles:
                response.Payload = JsonSerializer.SerializeToElement(new ProfilesPayload
                {
                    ActiveProfileId = _profiles.Document.ActiveProfileId,
                    Profiles = _profiles.GetProfiles().ToList(),
                    ProfileBindings = _profiles.GetBindings().ToList()
                }, IpcProtocol.JsonOptions);
                break;

            case IpcCommands.GetDriverCapabilities:
                response.Payload = JsonSerializer.SerializeToElement(
                    _bridge.Drivers.GetCapabilities(), IpcProtocol.JsonOptions);
                break;

            case IpcCommands.SetActiveProfile:
            {
                var p = Deserialize<SetActiveProfilePayload>(request.Payload);
                _bridge.SetActiveProfileManual(p.ProfileId);
                response.Payload = JsonSerializer.SerializeToElement(_bridge.Status, IpcProtocol.JsonOptions);
                break;
            }

            case IpcCommands.UpsertProfile:
            {
                var profile = Deserialize<ControllerProfile>(request.Payload);
                var saved = _profiles.Upsert(profile);
                response.Payload = JsonSerializer.SerializeToElement(saved, IpcProtocol.JsonOptions);
                break;
            }

            case IpcCommands.DeleteProfile:
            {
                var p = Deserialize<SetActiveProfilePayload>(request.Payload);
                _profiles.Delete(p.ProfileId);
                break;
            }

            case IpcCommands.RemapButton:
            {
                var p = Deserialize<RemapButtonPayload>(request.Payload);
                if (!Enum.TryParse<PhysicalInput>(p.Physical, true, out var phys))
                    throw new ArgumentException("Invalid physical input");
                if (!Enum.TryParse<XboxOutput>(p.Xbox, true, out var xbox))
                    throw new ArgumentException("Invalid xbox output");
                _profiles.Remap(p.ProfileId, phys, xbox);
                break;
            }

            case IpcCommands.RemapAction:
            {
                var p = Deserialize<RemapActionPayload>(request.Payload);
                _profiles.RemapAction(p.ProfileId, p.InputId, p.Action ?? OutputAction.None());
                break;
            }

            case IpcCommands.SetBridgeEnabled:
            {
                var p = Deserialize<SetBridgeEnabledPayload>(request.Payload);
                _bridge.SetEnabled(p.Enabled);
                response.Payload = JsonSerializer.SerializeToElement(_bridge.Status, IpcProtocol.JsonOptions);
                break;
            }

            case IpcCommands.SetAutoPauseWhenSteam:
            {
                var p = Deserialize<SetAutoPauseWhenSteamPayload>(request.Payload);
                _profiles.AutoPauseWhenSteamRunning = p.Enabled;
                response.Payload = JsonSerializer.SerializeToElement(_bridge.Status, IpcProtocol.JsonOptions);
                break;
            }

            case IpcCommands.SetTrackpadMode:
            {
                var p = Deserialize<SetTrackpadModePayload>(request.Payload);
                if (!Enum.TryParse<TrackpadMode>(p.Mode, true, out var mode))
                    throw new ArgumentException("Invalid trackpad mode");
                _profiles.SetTrackpad(p.ProfileId, p.Left, mode);
                break;
            }

            case IpcCommands.SetGyroMode:
            {
                var p = Deserialize<SetGyroModePayload>(request.Payload);
                if (!Enum.TryParse<GyroMode>(p.Mode, true, out var mode))
                    throw new ArgumentException("Invalid gyro mode");
                _profiles.SetGyro(p.ProfileId, mode, p.Sensitivity);
                break;
            }

            case IpcCommands.BindProfileToGame:
            {
                var p = Deserialize<BindProfileToGamePayload>(request.Payload);
                _profiles.BindToGame(p.ProfileId, p.MatchExe, p.MatchPathContains, p.DisplayName);
                break;
            }

            default:
                response.Ok = false;
                response.Error = $"Unknown command: {request.Command}";
                break;
        }
        return response;
    }

    static T Deserialize<T>(JsonElement? element)
    {
        if (element is null) throw new ArgumentException("Missing payload");
        return element.Value.Deserialize<T>(IpcProtocol.JsonOptions)
               ?? throw new ArgumentException("Invalid payload");
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _listenTask?.Wait(500); } catch { }
        _cts.Dispose();
    }
}
