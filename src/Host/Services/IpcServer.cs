using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using MistMapper.Shared;

namespace MistMapper.Host.Services;

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
                    _bridge.GetActiveCapabilities(), IpcProtocol.JsonOptions);
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

            case IpcCommands.SetBinding:
            {
                var p = Deserialize<SetBindingPayload>(request.Payload);
                if (!Enum.TryParse<ActivatorType>(p.Activator, true, out var activator))
                    throw new ArgumentException("Invalid activator");
                _profiles.RemapBindingAction(
                    p.ProfileId, p.InputId, activator, p.Slot, p.Action ?? OutputAction.None());
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

            case IpcCommands.SetSensitivity:
            {
                var p = Deserialize<SensitivityPayload>(request.Payload);
                _profiles.SetSensitivity(p.ProfileId, p);
                break;
            }

            case IpcCommands.GetControllerSlots:
            {
                var slots = _profiles.GetControllerSlots();
                response.Payload = JsonSerializer.SerializeToElement(slots, IpcProtocol.JsonOptions);
                break;
            }

            case IpcCommands.SetControllerSlotOrder:
            {
                var p = Deserialize<SetControllerSlotOrderPayload>(request.Payload);
                _profiles.SetControllerSlotOrder(p.Slots);
                break;
            }

            case IpcCommands.SetControllerSlotProfile:
            {
                var p = Deserialize<SetControllerSlotProfilePayload>(request.Payload);
                if (!string.IsNullOrWhiteSpace(p.DeviceKey))
                    _profiles.SetControllerSlotProfile(p.DeviceKey, p.ProfileId);
                else
                    _profiles.SetControllerSlotProfileByDriver(p.DriverId, p.ProfileId);
                break;
            }

            case IpcCommands.SetSelectedController:
            {
                var p = Deserialize<SetSelectedControllerPayload>(request.Payload);
                _bridge.SetSelectedController(p.DeviceKey);
                break;
            }

            case IpcCommands.MakeControllerProfileUnique:
            {
                var p = Deserialize<MakeControllerProfileUniquePayload>(request.Payload);
                var id = _bridge.MakeControllerProfileUnique(p.DeviceKey, p.SourceProfileId);
                response.Payload = JsonSerializer.SerializeToElement(new { profileId = id }, IpcProtocol.JsonOptions);
                break;
            }

            case IpcCommands.IdentifyController:
            {
                var p = Deserialize<IdentifyControllerPayload>(request.Payload);
                _bridge.IdentifyControllerAsync(p.DeviceKey).GetAwaiter().GetResult();
                break;
            }

            case IpcCommands.RenameController:
            {
                var p = Deserialize<RenameControllerPayload>(request.Payload);
                _bridge.RenameController(p.DeviceKey, p.DisplayName);
                response.Payload = JsonSerializer.SerializeToElement(_bridge.Status, IpcProtocol.JsonOptions);
                break;
            }

            case IpcCommands.SetControllerRumble:
            {
                var p = Deserialize<SetControllerRumblePayload>(request.Payload);
                _bridge.SetControllerRumbleEnabled(p.DeviceKey, p.RumbleEnabled);
                response.Payload = JsonSerializer.SerializeToElement(_bridge.Status, IpcProtocol.JsonOptions);
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
