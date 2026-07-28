using HidSharp;
using HidSharp.Reports;

namespace SteamControllerBridge.Host.Steam;

/// <summary>
/// Opens the Steam Controller 2026 vendor HID interface, toggles lizard mode,
/// and reads state reports.
/// </summary>
public sealed class SteamControllerDevice : IDisposable
{
    public const int ValveVid = 0x28DE;
    public static readonly int[] ProductIds = [0x1302, 0x1303, 0x1304];

    public const int VendorUsagePage = 0xFF00;

    const byte FeatureReportCmd = 0x01;
    const byte FeatureReportCmd2 = 0x02;
    const byte CmdClearDigitalMappings = 0x81;
    const byte CmdSetDefaultMappings = 0x85;
    const byte CmdSetSettings = 0x87;
    const byte CmdLoadDefaultSettings = 0x8E;
    const byte SettingRightTrackpadMode = 0x07;
    const byte SettingLeftTrackpadMode = 0x08;
    const byte SettingImuMode = 0x30;
    const byte TrackpadNone = 0x00;

    HidDevice? _device;
    HidStream? _stream;
    readonly object _writeLock = new();
    readonly byte[] _readBuffer = new byte[64];

    public bool IsOpen => _stream is not null;
    public string? DevicePath => _device?.DevicePath;

    public static IEnumerable<HidDevice> Enumerate()
    {
        var results = new List<HidDevice>();
        foreach (var dev in DeviceList.Local.GetHidDevices(ValveVid))
        {
            if (!ProductIds.Contains(dev.ProductID))
                continue;

            bool include = false;
            try
            {
                var report = dev.GetReportDescriptor();
                foreach (var deviceItem in report.DeviceItems)
                {
                    foreach (var usage in deviceItem.Usages.GetAllValues())
                    {
                        if ((usage >> 16) == VendorUsagePage)
                        {
                            include = true;
                            break;
                        }
                    }
                    if (include) break;
                }
                if (!include && dev.GetMaxInputReportLength() >= 30)
                    include = true;
            }
            catch
            {
                include = dev.GetMaxInputReportLength() >= 30;
            }

            if (include)
                results.Add(dev);
        }
        return results;
    }

    public bool Open()
    {
        Close();
        foreach (var dev in Enumerate())
        {
            if (TryOpen(dev))
                return true;
        }
        return false;
    }

    bool TryOpen(HidDevice device)
    {
        try
        {
            if (!device.TryOpen(out var stream))
                return false;
            stream.ReadTimeout = 50;
            stream.WriteTimeout = 200;
            _device = device;
            _stream = stream;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Close()
    {
        try { _stream?.Dispose(); } catch { /* ignore */ }
        _stream = null;
        _device = null;
    }

    public bool DisableLizardMode()
    {
        if (_stream is null) return false;
        bool ok = SendCommand(CmdClearDigitalMappings, ReadOnlySpan<byte>.Empty);
        // Trackpads to NONE so firmware stops mouse emulation.
        Span<byte> settings = stackalloc byte[9];
        settings[0] = SettingLeftTrackpadMode;
        settings[1] = TrackpadNone;
        settings[2] = 0;
        settings[3] = SettingRightTrackpadMode;
        settings[4] = TrackpadNone;
        settings[5] = 0;
        settings[6] = SettingImuMode;
        settings[7] = 0x18; // raw accel+gyro low byte
        settings[8] = 0x00;
        ok &= SendCommand(CmdSetSettings, settings);
        return ok;
    }

    public bool EnableLizardMode()
    {
        if (_stream is null) return false;
        bool ok = SendCommand(CmdSetDefaultMappings, ReadOnlySpan<byte>.Empty);
        ok &= SendCommand(CmdLoadDefaultSettings, ReadOnlySpan<byte>.Empty);
        return ok;
    }

    public bool SendKeepalive() => DisableLizardMode();

    public bool TryReadState(out SteamControllerBridge.Shared.SteamControllerState state, int timeoutMs = 50)
    {
        state = new();
        if (_stream is null) return false;

        try
        {
            _stream.ReadTimeout = timeoutMs;
            int read = _stream.Read(_readBuffer, 0, _readBuffer.Length);
            if (read <= 0) return false;
            return SteamReportParser.TryParse(_readBuffer.AsSpan(0, read), out state);
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch (IOException)
        {
            Close();
            return false;
        }
        catch (ObjectDisposedException)
        {
            Close();
            return false;
        }
    }

    bool SendCommand(byte command, ReadOnlySpan<byte> payload)
    {
        if (_stream is null) return false;

        // Feature report layout: [reportId | cmd | size | payload...]
        int max = Math.Max(_device?.GetMaxFeatureReportLength() ?? 64, 64);
        var buffer = new byte[max];
        buffer[0] = FeatureReportCmd;
        buffer[1] = command;
        buffer[2] = (byte)payload.Length;
        payload.CopyTo(buffer.AsSpan(3));

        lock (_writeLock)
        {
            try
            {
                _stream.SetFeature(buffer);
                return true;
            }
            catch
            {
                try
                {
                    buffer[0] = FeatureReportCmd2;
                    _stream.SetFeature(buffer);
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }
    }

    public void Dispose() => Close();
}
