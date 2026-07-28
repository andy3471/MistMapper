using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace SteamControllerBridge.Host.Viiper;

public sealed class Xbox360InputState
{
    public uint Buttons { get; set; }
    public byte LeftTrigger { get; set; }
    public byte RightTrigger { get; set; }
    public short ThumbLX { get; set; }
    public short ThumbLY { get; set; }
    public short ThumbRX { get; set; }
    public short ThumbRY { get; set; }

    public byte[] ToBytes()
    {
        var b = new byte[20];
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(0), Buttons);
        b[4] = LeftTrigger;
        b[5] = RightTrigger;
        BinaryPrimitives.WriteInt16LittleEndian(b.AsSpan(6), ThumbLX);
        BinaryPrimitives.WriteInt16LittleEndian(b.AsSpan(8), ThumbLY);
        BinaryPrimitives.WriteInt16LittleEndian(b.AsSpan(10), ThumbRX);
        BinaryPrimitives.WriteInt16LittleEndian(b.AsSpan(12), ThumbRY);
        return b;
    }
}

[Flags]
public enum Xbox360Buttons : uint
{
    DpadUp = 0x0001,
    DpadDown = 0x0002,
    DpadLeft = 0x0004,
    DpadRight = 0x0008,
    Start = 0x0010,
    Back = 0x0020,
    LeftThumb = 0x0040,
    RightThumb = 0x0080,
    LeftShoulder = 0x0100,
    RightShoulder = 0x0200,
    Guide = 0x0400,
    A = 0x1000,
    B = 0x2000,
    X = 0x4000,
    Y = 0x8000
}

/// <summary>Minimal VIIPER TCP client for creating and feeding an xbox360 device.</summary>
public sealed class ViiperXbox360Client : IDisposable
{
    readonly string _host;
    readonly int _apiPort;
    TcpClient? _mgmt;
    TcpClient? _stream;
    NetworkStream? _deviceStream;
    int _busId;
    string? _devId;

    public bool IsConnected => _deviceStream is not null;
    public int BusId => _busId;
    public string? DeviceId => _devId;
    public event Action<byte, byte>? RumbleReceived;

    public ViiperXbox360Client(string host = "127.0.0.1", int apiPort = 3242)
    {
        _host = host;
        _apiPort = apiPort;
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        Disconnect();

        _mgmt = new TcpClient();
        await _mgmt.ConnectAsync(_host, _apiPort, ct);

        // Create bus
        var createResp = await SendMgmtAsync("bus/create", ct);
        using (var doc = JsonDocument.Parse(createResp))
            _busId = doc.RootElement.GetProperty("busId").GetInt32();

        // Add xbox360 device
        var addResp = await SendMgmtAsync($"bus/{_busId}/add {{\"type\":\"xbox360\"}}", ct);
        using (var doc = JsonDocument.Parse(addResp))
        {
            if (doc.RootElement.TryGetProperty("devId", out var idEl))
                _devId = idEl.ValueKind == JsonValueKind.Number
                    ? idEl.GetInt32().ToString()
                    : idEl.GetString();
            else if (doc.RootElement.TryGetProperty("id", out var id2))
                _devId = id2.ToString();
            else
                throw new InvalidOperationException($"Unexpected add response: {addResp}");
        }

        // Device stream handshake
        _stream = new TcpClient();
        await _stream.ConnectAsync(_host, _apiPort, ct);
        _deviceStream = _stream.GetStream();
        var handshake = Encoding.UTF8.GetBytes($"bus/{_busId}/{_devId}\0");
        await _deviceStream.WriteAsync(handshake, ct);
        await _deviceStream.FlushAsync(ct);

        _ = Task.Run(() => ReadRumbleLoop(ct), ct);
    }

    public async Task SendInputAsync(Xbox360InputState state, CancellationToken ct = default)
    {
        if (_deviceStream is null)
            throw new InvalidOperationException("Not connected");
        var bytes = state.ToBytes();
        await _deviceStream.WriteAsync(bytes, ct);
    }

    public void SendInput(Xbox360InputState state)
    {
        if (_deviceStream is null) return;
        var bytes = state.ToBytes();
        _deviceStream.Write(bytes);
    }

    async Task ReadRumbleLoop(CancellationToken ct)
    {
        var buf = new byte[2];
        try
        {
            while (!ct.IsCancellationRequested && _deviceStream is not null)
            {
                int n = await _deviceStream.ReadAsync(buf.AsMemory(0, 2), ct);
                if (n < 2) break;
                RumbleReceived?.Invoke(buf[0], buf[1]);
            }
        }
        catch
        {
            // stream closed
        }
    }

    async Task<string> SendMgmtAsync(string request, CancellationToken ct)
    {
        if (_mgmt is null) throw new InvalidOperationException("Management socket not open");
        var stream = _mgmt.GetStream();
        var payload = Encoding.UTF8.GetBytes(request + "\0");
        await stream.WriteAsync(payload, ct);
        await stream.FlushAsync(ct);

        var buffer = new List<byte>();
        var tmp = new byte[1];
        while (true)
        {
            int n = await stream.ReadAsync(tmp.AsMemory(0, 1), ct);
            if (n == 0) break;
            if (tmp[0] == 0) break;
            buffer.Add(tmp[0]);
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    public void Disconnect()
    {
        try
        {
            if (_mgmt is not null && _busId > 0 && _devId is not null)
            {
                try
                {
                    var stream = _mgmt.GetStream();
                    var payload = Encoding.UTF8.GetBytes($"bus/{_busId}/remove {_devId}\0");
                    stream.Write(payload);
                }
                catch { /* ignore */ }
            }
        }
        finally
        {
            try { _deviceStream?.Dispose(); } catch { }
            try { _stream?.Dispose(); } catch { }
            try { _mgmt?.Dispose(); } catch { }
            _deviceStream = null;
            _stream = null;
            _mgmt = null;
            _devId = null;
        }
    }

    public void Dispose() => Disconnect();
}
