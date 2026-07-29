using HidSharp;
using HidSharp.Reports;

namespace MistMapper.Host.Steam;

/// <summary>
/// Opens the Steam Controller 2026 vendor HID interface, toggles lizard mode,
/// and reads state reports.
/// </summary>
public sealed class SteamControllerDevice : IDisposable
{
    public const int ValveVid = 0x28DE;
    /// <summary>Steam Controller 2026 (SC2) product IDs.</summary>
    public static readonly int[] Sc2ProductIds = [0x1302, 0x1303, 0x1304];
    /// <summary>Original Steam Controller 2015 (SC1) product IDs.</summary>
    public static readonly int[] Sc1ProductIds = [0x1102, 0x1142];
    /// <summary>IDs the host currently opens (SC2). SC1 listed for model detection / future support.</summary>
    public static readonly int[] ProductIds = Sc2ProductIds;

    public const int VendorUsagePage = 0xFF00;

    const byte FeatureReportCmd = 0x01;
    const byte FeatureReportCmd2 = 0x02;
    const byte CmdClearDigitalMappings = 0x81;
    const byte CmdSetDefaultMappings = 0x85;
    const byte CmdSetSettings = 0x87;
    const byte CmdLoadDefaultSettings = 0x8E;
    // Valve settings IDs (see linux hid-steam.c)
    const byte SettingLeftTrackpadMode = 0x07;
    const byte SettingRightTrackpadMode = 0x08;
    const byte SettingImuMode = 0x30;
    const byte SettingSteamWatchdogEnable = 0x47; // 71
    /// <summary>TRACKPAD_NONE in Valve firmware — not 0 (0 = ABSOLUTE_MOUSE).</summary>
    const byte TrackpadNone = 0x07;

    HidDevice? _device;
    HidStream? _stream;
    readonly object _writeLock = new();
    readonly byte[] _readBuffer = new byte[64];

    public bool IsOpen => _stream is not null;
    public string? DevicePath => _device?.DevicePath;
    public int ProductId => _device?.ProductID ?? 0;

    public static string ClassifyModel(int productId)
    {
        if (Sc2ProductIds.Contains(productId)) return "sc2";
        if (Sc1ProductIds.Contains(productId)) return "sc1";
        return "";
    }

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
                if (dev.GetMaxFeatureReportLength() < 64)
                    continue;

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
                include = dev.GetMaxInputReportLength() >= 30
                    && dev.GetMaxFeatureReportLength() >= 64;
            }

            if (include)
                results.Add(dev);
        }

        // Prefer collections that typically accept lizard-mode feature reports (col03).
        return results
            .OrderByDescending(d => d.DevicePath?.Contains("col03", StringComparison.OrdinalIgnoreCase) == true)
            .ThenBy(d => d.DevicePath, StringComparer.OrdinalIgnoreCase);
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
        HidStream? stream = null;
        try
        {
            if (!device.TryOpen(out stream))
                return false;
            stream.ReadTimeout = 50;
            stream.WriteTimeout = 200;
            _device = device;
            _stream = stream;

            // SC2 exposes several vendor-looking HID collections; only some accept
            // feature reports. Reject interfaces where lizard disable cannot be sent,
            // otherwise we "connect" while keyboard/mouse lizard mode stays on.
            if (!DisableLizardMode())
            {
                Close();
                return false;
            }

            return true;
        }
        catch
        {
            try { stream?.Dispose(); } catch { /* ignore */ }
            _stream = null;
            _device = null;
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
        // Also disable the Steam-presence watchdog (Deck/SC2) that re-enables lizard.
        Span<byte> settings = stackalloc byte[12];
        settings[0] = SettingLeftTrackpadMode;
        settings[1] = TrackpadNone;
        settings[2] = 0;
        settings[3] = SettingRightTrackpadMode;
        settings[4] = TrackpadNone;
        settings[5] = 0;
        settings[6] = SettingSteamWatchdogEnable;
        settings[7] = 0;
        settings[8] = 0;
        settings[9] = SettingImuMode;
        settings[10] = 0x18; // raw accel+gyro low byte
        settings[11] = 0x00;
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

    public bool TryReadState(out MistMapper.Shared.SteamControllerState state, int timeoutMs = 50)
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
