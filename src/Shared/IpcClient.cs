using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace SteamControllerBridge.Shared;

public sealed class IpcClient : IDisposable
{
    NamedPipeClientStream? _pipe;
    StreamReader? _reader;
    StreamWriter? _writer;
    readonly SemaphoreSlim _gate = new(1, 1);

    public async Task ConnectAsync(int timeoutMs = 2000, CancellationToken ct = default)
    {
        Disconnect();
        _pipe = new NamedPipeClientStream(".", IpcProtocol.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await _pipe.ConnectAsync(timeoutMs, ct);
        _reader = new StreamReader(_pipe, Encoding.UTF8, leaveOpen: true);
        _writer = new StreamWriter(_pipe, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
    }

    public bool IsConnected => _pipe?.IsConnected == true;

    public async Task<IpcResponse> SendAsync(string command, object? payload = null, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_writer is null || _reader is null || _pipe is null || !_pipe.IsConnected)
                throw new InvalidOperationException("Not connected to host IPC");

            var request = new IpcRequest
            {
                Command = command,
                Payload = payload is null
                    ? null
                    : JsonSerializer.SerializeToElement(payload, IpcProtocol.JsonOptions)
            };
            var line = JsonSerializer.Serialize(request, IpcProtocol.JsonOptions);
            await _writer.WriteLineAsync(line.AsMemory(), ct);
            var responseLine = await _reader.ReadLineAsync(ct)
                               ?? throw new IOException("IPC pipe closed");
            return JsonSerializer.Deserialize<IpcResponse>(responseLine, IpcProtocol.JsonOptions)
                   ?? throw new InvalidOperationException("Invalid IPC response");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<BridgeStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var resp = await SendAsync(IpcCommands.GetStatus, ct: ct);
        EnsureOk(resp);
        return resp.Payload!.Value.Deserialize<BridgeStatus>(IpcProtocol.JsonOptions)!;
    }

    public async Task<ProfilesPayload> GetProfilesAsync(CancellationToken ct = default)
    {
        var resp = await SendAsync(IpcCommands.GetProfiles, ct: ct);
        EnsureOk(resp);
        return resp.Payload!.Value.Deserialize<ProfilesPayload>(IpcProtocol.JsonOptions)!;
    }

    static void EnsureOk(IpcResponse resp)
    {
        if (!resp.Ok) throw new InvalidOperationException(resp.Error ?? "IPC error");
    }

    public void Disconnect()
    {
        try { _writer?.Dispose(); } catch { }
        try { _reader?.Dispose(); } catch { }
        try { _pipe?.Dispose(); } catch { }
        _writer = null;
        _reader = null;
        _pipe = null;
    }

    public void Dispose()
    {
        Disconnect();
        _gate.Dispose();
    }
}
