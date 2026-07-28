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

/// <summary>
/// VIIPER TCP client. Management API = one TCP connection per command; response ends on socket close.
/// </summary>
public sealed class ViiperXbox360Client : IDisposable
{
    readonly string _host;
    readonly int _apiPort;
    TcpClient? _stream;
    NetworkStream? _deviceStream;
    CancellationTokenSource? _rumbleCts;
    int _busId;
    string? _devId;
    bool _connected;

    public bool IsConnected => _connected && _deviceStream is not null;
    public int BusId => _busId;
    public string? DeviceId => _devId;
    public event Action<byte, byte>? RumbleReceived;
    public static string? LastError { get; private set; }

    public ViiperXbox360Client(string host = "127.0.0.1", int apiPort = 3242)
    {
        _host = host;
        _apiPort = apiPort;
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        LastError = null;
        await TearDownAsync(removeBus: true, ct).ConfigureAwait(false);

        try
        {
            var createResp = await SendMgmtAsync("bus/create", ct).ConfigureAwait(false);
            EnsureNotError(createResp, "bus/create");
            using (var doc = JsonDocument.Parse(createResp))
                _busId = doc.RootElement.GetProperty("busId").GetInt32();

            var addResp = await SendMgmtAsync(
                $"bus/{_busId}/add {{\"type\":\"xbox360\"}}", ct).ConfigureAwait(false);
            EnsureNotError(addResp, "bus/add xbox360");
            using (var doc = JsonDocument.Parse(addResp))
            {
                if (doc.RootElement.TryGetProperty("devId", out var idEl))
                    _devId = idEl.ValueKind == JsonValueKind.Number
                        ? idEl.GetInt32().ToString()
                        : idEl.GetString();
                else
                    throw new InvalidOperationException($"Unexpected add response: {addResp}");
            }

            if (string.IsNullOrEmpty(_devId))
                throw new InvalidOperationException($"VIIPER returned empty device id: {addResp}");

            _stream = new TcpClient();
            await _stream.ConnectAsync(_host, _apiPort, ct).ConfigureAwait(false);
            _deviceStream = _stream.GetStream();
            var handshake = Encoding.UTF8.GetBytes($"bus/{_busId}/{_devId}\0");
            await _deviceStream.WriteAsync(handshake, ct).ConfigureAwait(false);
            await _deviceStream.FlushAsync(ct).ConfigureAwait(false);

            TryUsbipAttach($"{_busId}-{_devId}");

            // Rumble CTS must outlive the connect timeout token — linking them caused
            // IsConnected to flip false as soon as ConnectAsync returned, so the bridge
            // loop reconnect forever and stuck on "Connecting to VIIPER…".
            _rumbleCts = new CancellationTokenSource();
            _connected = true;
            _ = Task.Run(() => ReadRumbleLoop(_rumbleCts.Token), CancellationToken.None);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            try { await TearDownAsync(removeBus: true, CancellationToken.None).ConfigureAwait(false); }
            catch { /* ignore */ }
            throw;
        }
    }

    public void SendInput(Xbox360InputState state)
    {
        if (!_connected || _deviceStream is null) return;
        try { _deviceStream.Write(state.ToBytes()); }
        catch
        {
            _connected = false;
        }
    }

    async Task ReadRumbleLoop(CancellationToken ct)
    {
        var buf = new byte[2];
        try
        {
            while (!ct.IsCancellationRequested && _deviceStream is not null)
            {
                int n = await _deviceStream.ReadAsync(buf.AsMemory(0, 2), ct).ConfigureAwait(false);
                if (n < 2) break;
                RumbleReceived?.Invoke(buf[0], buf[1]);
            }

            // Stream ended unexpectedly (not a TearDown cancel).
            if (!ct.IsCancellationRequested)
                _connected = false;
        }
        catch (OperationCanceledException) { /* TearDown */ }
        catch
        {
            if (!ct.IsCancellationRequested)
                _connected = false;
        }
    }

    async Task<string> SendMgmtAsync(string request, CancellationToken ct)
    {
        using var client = new TcpClient();
        // Prefer IPv4 explicitly
        await client.ConnectAsync(System.Net.IPAddress.Loopback, _apiPort, ct).ConfigureAwait(false);
        await using var stream = client.GetStream();

        var payload = Encoding.UTF8.GetBytes(request + "\0");
        await stream.WriteAsync(payload, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);

        using var ms = new MemoryStream();
        var tmp = new byte[512];
        while (true)
        {
            int n;
            try
            {
                n = await stream.ReadAsync(tmp.AsMemory(0, tmp.Length), ct).ConfigureAwait(false);
            }
            catch (IOException) when (ms.Length > 0)
            {
                break;
            }

            if (n == 0) break;

            int nullAt = Array.IndexOf(tmp, (byte)0, 0, n);
            if (nullAt >= 0)
            {
                if (nullAt > 0) ms.Write(tmp, 0, nullAt);
                break;
            }
            ms.Write(tmp, 0, n);
        }

        var text = Encoding.UTF8.GetString(ms.ToArray()).Trim();
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException(
                $"Empty response from VIIPER for '{request}'. Is usbip-win2 installed and is usbip.exe on PATH?");
        return text;
    }

    static void EnsureNotError(string json, string op)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("status", out var status) ||
            status.ValueKind != JsonValueKind.Number)
            return;

        var code = status.GetInt32();
        if (code < 400) return;

        var detail = doc.RootElement.TryGetProperty("detail", out var d) ? d.GetString() : json;
        if (detail?.Contains("usbip", StringComparison.OrdinalIgnoreCase) == true)
            throw new InvalidOperationException(
                "usbip-win2 missing or not on PATH. Install USBip from https://github.com/vadimgrn/usbip-win2 " +
                @"and ensure ""C:\Program Files\USBip"" is on PATH, then restart viiper. Detail: " + detail);

        throw new InvalidOperationException($"VIIPER {op} failed ({code}): {detail}");
    }

    static void TryUsbipAttach(string busDev)
    {
        var usbip = @"C:\Program Files\USBip\usbip.exe";
        if (!File.Exists(usbip))
        {
            var path = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var dir in path.Split(Path.PathSeparator))
            {
                var candidate = Path.Combine(dir.Trim('"'), "usbip.exe");
                if (File.Exists(candidate)) { usbip = candidate; break; }
            }
        }
        if (!File.Exists(usbip)) return;

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = usbip,
                Arguments = $"attach -r 127.0.0.1 -b {busDev}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            using var p = System.Diagnostics.Process.Start(psi);
            if (p is null) return;
            if (!p.WaitForExit(5000))
            {
                try { p.Kill(entireProcessTree: true); } catch { /* ignore */ }
            }
        }
        catch
        {
            // VHCI may be broken; feeder stream still works for diagnostics.
        }
    }

    async Task TearDownAsync(bool removeBus, CancellationToken ct)
    {
        _connected = false;
        try { _rumbleCts?.Cancel(); } catch { }

        try { _deviceStream?.Dispose(); } catch { }
        try { _stream?.Dispose(); } catch { }
        _deviceStream = null;
        _stream = null;

        if (removeBus && _busId > 0)
        {
            var bus = _busId;
            var dev = _devId;
            _busId = 0;
            _devId = null;
            try
            {
                if (!string.IsNullOrEmpty(dev))
                    await SendMgmtAsync($"bus/{bus}/remove {dev}", ct).ConfigureAwait(false);
            }
            catch { /* ignore */ }
            try
            {
                await SendMgmtAsync($"bus/remove {bus}", ct).ConfigureAwait(false);
            }
            catch { /* ignore */ }
        }
        else
        {
            _busId = 0;
            _devId = null;
        }

        try { _rumbleCts?.Dispose(); } catch { }
        _rumbleCts = null;
    }

    public void Disconnect()
    {
        try
        {
            TearDownAsync(removeBus: true, CancellationToken.None)
                .Wait(TimeSpan.FromSeconds(2));
        }
        catch { /* ignore */ }
    }

    public void Dispose() => Disconnect();
}
