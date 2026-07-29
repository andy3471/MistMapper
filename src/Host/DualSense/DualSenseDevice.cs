using HidSharp;
using MistMapper.Host.Steam;

namespace MistMapper.Host.DualSense;

/// <summary>
/// Opens a DualSense / DualSense Edge HID interface, enables BT full reports when needed,
/// and reads state / writes rumble.
/// </summary>
public sealed class DualSenseDevice : IDisposable
{
    public const int SonyVid = 0x054C;
    public static readonly int[] ProductIds = [0x0CE6, 0x0DF2];

    const byte FeatureCalibration = 0x05;
    const byte UsbEffectsReportId = 0x02;
    const byte BtEffectsReportId = 0x31;

    HidDevice? _device;
    HidStream? _stream;
    readonly object _writeLock = new();
    readonly byte[] _readBuffer = new byte[128];
    bool _bluetooth;
    bool _enhanced;
    readonly NativeGamepadHider _nativeHider = new();

    public bool IsOpen => _stream is not null;
    public string? DevicePath => _device?.DevicePath;
    public int ProductId => _device?.ProductID ?? 0;
    public string Model => ClassifyModel(ProductId);
    public bool IsBluetooth => _bluetooth;

    public static string ClassifyModel(int productId) => productId switch
    {
        0x0DF2 => "dualsense-edge",
        0x0CE6 => "dualsense",
        _ => ""
    };

    public static string DisplayNameForModel(string model) => model switch
    {
        "dualsense-edge" => "DualSense Edge",
        "dualsense" => "DualSense",
        _ => "DualSense"
    };

    public static IReadOnlyList<HidDevice> EnumerateInstances(IEnumerable<string>? excludeDeviceKeys = null)
    {
        var exclude = new HashSet<string>(excludeDeviceKeys ?? [], StringComparer.OrdinalIgnoreCase);
        var bestPerPad = new Dictionary<string, HidDevice>(StringComparer.OrdinalIgnoreCase);

        foreach (var dev in Enumerate())
        {
            if (string.IsNullOrEmpty(dev.DevicePath)) continue;
            var key = PhysicalDeviceKey(dev.DevicePath!);
            if (exclude.Contains(key)) continue;
            if (!bestPerPad.TryGetValue(key, out var existing)
                || Prefer(dev, existing))
                bestPerPad[key] = dev;
        }

        return bestPerPad.Values
            .OrderBy(d => d.DevicePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    static bool Prefer(HidDevice candidate, HidDevice existing)
    {
        try
        {
            return candidate.GetMaxInputReportLength() > existing.GetMaxInputReportLength();
        }
        catch
        {
            return false;
        }
    }

    public static string PhysicalDeviceKey(string devicePath)
    {
        if (DeviceContainerId.TryGet(devicePath, out var containerId))
            return "container:" + containerId.ToString("D");
        return devicePath ?? "";
    }

    public static bool IsBluetoothPath(string? devicePath) =>
        !string.IsNullOrEmpty(devicePath)
        && devicePath.Contains("bth", StringComparison.OrdinalIgnoreCase);

    public static IEnumerable<HidDevice> Enumerate()
    {
        foreach (var dev in DeviceList.Local.GetHidDevices(SonyVid))
        {
            if (!ProductIds.Contains(dev.ProductID))
                continue;
            try
            {
                // Prefer interfaces that can carry full DualSense reports.
                if (dev.GetMaxInputReportLength() < 48)
                    continue;
            }
            catch
            {
                continue;
            }
            yield return dev;
        }
    }

    public bool Open()
    {
        foreach (var dev in EnumerateInstances())
        {
            if (Open(dev))
                return true;
        }
        return false;
    }

    public bool Open(string devicePathOrKey)
    {
        if (string.IsNullOrWhiteSpace(devicePathOrKey))
            return Open();

        foreach (var dev in Enumerate())
        {
            if (string.IsNullOrEmpty(dev.DevicePath)) continue;
            if (string.Equals(dev.DevicePath, devicePathOrKey, StringComparison.OrdinalIgnoreCase)
                || string.Equals(PhysicalDeviceKey(dev.DevicePath!), devicePathOrKey, StringComparison.OrdinalIgnoreCase))
            {
                if (Open(dev))
                    return true;
            }
        }
        return false;
    }

    public bool Open(HidDevice device)
    {
        Close();
        HidStream? stream = null;
        try
        {
            if (!device.TryOpen(out stream))
                return false;
            stream.ReadTimeout = 50;
            stream.WriteTimeout = 200;
            _device = device;
            _stream = stream;
            _bluetooth = IsBluetoothPath(device.DevicePath);
            _enhanced = false;

            // Best-effort calibration / BT enhance kick.
            TryEnableEnhancedMode();
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
        // Re-show the native gamepad before releasing our HID handle.
        RestoreNativeGamepad();
        try { WriteRumble(0, 0); } catch { /* ignore */ }
        try { _stream?.Dispose(); } catch { /* ignore */ }
        _stream = null;
        _device = null;
        _enhanced = false;
    }

    /// <summary>
    /// Disable Windows' standard DualSense gamepad interface so games only see VIIPER.
    /// Requires the host process to be elevated; returns false if hide failed.
    /// </summary>
    public bool HideNativeGamepad()
    {
        var path = _device?.DevicePath;
        if (string.IsNullOrEmpty(path)) return false;
        return _nativeHider.TryHideForDevice(path);
    }

    public void RestoreNativeGamepad() => _nativeHider.Restore();

    public bool NativeGamepadHidden => _nativeHider.HasHidden;

    void TryEnableEnhancedMode()
    {
        if (_stream is null) return;

        // Feature 0x05 calibration read is part of BT bring-up on many stacks.
        try
        {
            var cal = new byte[64];
            cal[0] = FeatureCalibration;
            _stream.GetFeature(cal);
        }
        catch
        {
            // USB often works without this; BT may still enhance via effects write.
        }

        // Sending an effects report switches BT into full 0x31 reports (SDL enhanced mode).
        if (WriteRumble(0, 0))
            _enhanced = true;
    }

    public bool TryReadState(out DualSenseState state, int timeoutMs = 50)
    {
        state = new();
        if (_stream is null) return false;

        try
        {
            _stream.ReadTimeout = timeoutMs;
            int read = _stream.Read(_readBuffer, 0, _readBuffer.Length);
            if (read <= 0) return false;
            var span = _readBuffer.AsSpan(0, read);

            // Ignore short BT simple reports once enhanced mode is on.
            if (_enhanced && span[0] == DualSenseReportParser.UsbReportId && read < 48)
                return false;

            return DualSenseReportParser.TryParse(span, out state);
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

    public void SetRumble(byte leftMotor, byte rightMotor) => WriteRumble(leftMotor, rightMotor);

    public async Task<bool> IdentifyAsync(CancellationToken ct = default)
    {
        if (_stream is null) return false;

        // Pulse motors + lightbar so the pad is obvious even if one path fails.
        bool ok = WriteEffects(leftMotor: 0xC0, rightMotor: 0xC0, lightbarRgb: (0x00, 0x90, 0xFF));
        try { await Task.Delay(450, ct); }
        catch (OperationCanceledException) { /* restore */ }
        ok &= WriteEffects(leftMotor: 0, rightMotor: 0, lightbarRgb: (0x00, 0x00, 0x40));
        return ok;
    }

    bool WriteRumble(byte leftMotor, byte rightMotor) =>
        WriteEffects(leftMotor, rightMotor, lightbarRgb: null);

    /// <param name="lightbarRgb">When set, also updates lightbar color (R,G,B).</param>
    bool WriteEffects(byte leftMotor, byte rightMotor, (byte R, byte G, byte B)? lightbarRgb)
    {
        if (_stream is null) return false;

        // DS5EffectsState_t (47 bytes) — matches SDL / Linux hid-playstation layout.
        Span<byte> effects = stackalloc byte[47];
        effects.Clear();

        // Always select classic haptics path (bit1) so audio haptics don't steal the motors.
        effects[0] = 0x02; // EnableBits1: HAPTICS_SELECT

        if (leftMotor != 0 || rightMotor != 0)
        {
            // Old firmware: COMPATIBLE_VIBRATION (bit0). New (≥2.24): COMPATIBLE_VIBRATION2 in EnableBits3.
            // Set both so Identify/rumble works across firmware revisions.
            effects[0] |= 0x01;
            effects[38] = 0x04; // EnableBits3: improved rumble
            effects[2] = rightMotor; // right / high-freq
            effects[3] = leftMotor;  // left / low-freq
        }

        if (lightbarRgb is { } rgb)
        {
            effects[1] |= 0x04; // EnableBits2: LED color
            effects[44] = rgb.R;
            effects[45] = rgb.G;
            effects[46] = rgb.B;
        }

        lock (_writeLock)
        {
            try
            {
                if (_bluetooth)
                {
                    var buf = new byte[78];
                    buf[0] = BtEffectsReportId;
                    buf[1] = 0x00; // tag / sequence (SDL)
                    buf[2] = 0x10; // magic (SDL)
                    effects.CopyTo(buf.AsSpan(3));
                    DualSenseCrc32.WriteBluetoothOutputCrc(buf);
                    _stream.Write(buf);
                }
                else
                {
                    // SDL uses 48; Linux uses 63 — prefer the device's reported max when larger.
                    int len = 48;
                    try
                    {
                        int max = _device?.GetMaxOutputReportLength() ?? 48;
                        if (max > len) len = Math.Min(max, 64);
                    }
                    catch { /* keep 48 */ }

                    var buf = new byte[len];
                    buf[0] = UsbEffectsReportId;
                    effects.CopyTo(buf.AsSpan(1));
                    _stream.Write(buf);
                }
                return true;
            }
            catch
            {
                // Some stacks want SetFeature for USB effects — try once.
                try
                {
                    if (!_bluetooth)
                    {
                        var buf = new byte[48];
                        buf[0] = UsbEffectsReportId;
                        effects.CopyTo(buf.AsSpan(1));
                        _stream.SetFeature(buf);
                        return true;
                    }
                }
                catch { /* ignore */ }
                return false;
            }
        }
    }

    public void Dispose()
    {
        Close();
        _nativeHider.Dispose();
    }
}
