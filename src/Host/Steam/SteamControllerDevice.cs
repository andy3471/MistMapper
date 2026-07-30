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
    /// <summary>IDs the host opens: Steam Controller 2015 (SC1) and 2026 (SC2).</summary>
    public static readonly int[] ProductIds =
        Sc1ProductIds.Concat(Sc2ProductIds).Distinct().ToArray();

    public const int VendorUsagePage = 0xFF00;

    const byte FeatureReportCmd = 0x01;
    const byte FeatureReportCmd2 = 0x02;
    const byte CmdClearDigitalMappings = 0x81;
    const byte CmdSetDefaultMappings = 0x85;
    const byte CmdSetSettings = 0x87;
    const byte CmdLoadDefaultSettings = 0x8E;
    const byte CmdTriggerHapticPulse = 0x8F; // SC1 trackpad haptics (emulates rumble)
    const byte OutReportHapticRumble = 0x80; // SC2 Triton output report
    const byte OutReportHapticPulse = 0x81;
    const byte OutReportHapticCommand = 0x82;
    const byte HapticPadLeft = 0;
    const byte HapticPadRight = 1;
    const byte HapticPadBoth = 2;
    const byte TritonSideLeft = 0x01;
    const byte TritonSideRight = 0x02;
    const byte TritonHapticTick = 1;
    const byte TritonHapticClick = 2;
    // Valve settings IDs (see linux hid-steam.c)
    const byte SettingLeftTrackpadMode = 0x07;
    const byte SettingRightTrackpadMode = 0x08;
    const byte SettingImuMode = 0x30;
    const byte SettingWirelessPacketVersion = 0x31;
    const byte SettingSteamWatchdogEnable = 0x47; // 71
    /// <summary>Raw accel + raw gyro + orientation (classic SC IMU bitmask).</summary>
    const ushort ImuModeFull = 0x001C;
    /// <summary>TRACKPAD_NONE in Valve firmware — not 0 (0 = ABSOLUTE_MOUSE).</summary>
    const byte TrackpadNone = 0x07;

    HidDevice? _device;
    HidStream? _stream;
    readonly object _writeLock = new();
    readonly object _rumbleGate = new();
    byte _rumbleLeft;
    byte _rumbleRight;
    CancellationTokenSource? _rumbleCts;
    Task? _rumbleTask;
    readonly byte[] _readBuffer = new byte[128];

    public bool IsOpen => _stream is not null;
    public string? DevicePath => _device?.DevicePath;
    public int ProductId => _device?.ProductID ?? 0;
    public string Model => ClassifyModel(ProductId);

    public static string ClassifyModel(int productId)
    {
        if (Sc2ProductIds.Contains(productId)) return "sc2";
        if (Sc1ProductIds.Contains(productId)) return "sc1";
        return "";
    }

    /// <summary>
    /// Unique HID interfaces suitable for bridging (one preferred collection per physical pad).
    /// Paths already in <paramref name="excludeDeviceKeys"/> are skipped.
    /// </summary>
    public static IReadOnlyList<HidDevice> EnumerateInstances(IEnumerable<string>? excludeDeviceKeys = null)
    {
        var exclude = new HashSet<string>(excludeDeviceKeys ?? [], StringComparer.OrdinalIgnoreCase);
        // Group by physical device stem so we only open one collection per pad.
        var bestPerPad = new Dictionary<string, HidDevice>(StringComparer.OrdinalIgnoreCase);
        foreach (var dev in FilterBridgeInterfaces(Enumerate().ToList()))
        {
            if (string.IsNullOrEmpty(dev.DevicePath)) continue;
            var key = PhysicalDeviceKey(dev.DevicePath!);
            if (exclude.Contains(key)) continue;
            if (!bestPerPad.ContainsKey(key))
                bestPerPad[key] = dev;
        }

        return bestPerPad.Values
            .OrderBy(d => d.DevicePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// SC2 exposes several USB interfaces that all accept lizard-mode feature reports
    /// (e.g. mi_02&amp;col03 and a parallel mi_06). Prefer vendor collection col03 so one
    /// physical pad does not appear as multiple bridge slots.
    /// </summary>
    public static IReadOnlyList<HidDevice> FilterBridgeInterfaces(IReadOnlyList<HidDevice> candidates)
    {
        if (candidates.Count == 0) return candidates;

        var paths = candidates
            .Select(d => d.DevicePath ?? "")
            .Where(p => p.Length > 0)
            .ToList();
        var preferredPaths = new HashSet<string>(
            PreferBridgeInterfacePaths(paths),
            StringComparer.OrdinalIgnoreCase);

        return candidates
            .Where(d => !string.IsNullOrEmpty(d.DevicePath) && preferredPaths.Contains(d.DevicePath!))
            .OrderBy(d => MiNumber(d.DevicePath))
            .ThenBy(d => d.DevicePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// From a set of HID paths for Valve pads, pick the ones we should try to open.
    /// SC2 exposes many sibling interfaces (mi_02..mi_06); prefer mi_02&amp;col03 which is
    /// unique per physical pad. Fall back to any col03, then anything remaining.
    /// Finally collapse by <see cref="PhysicalDeviceKey"/> (Container ID when available).
    /// </summary>
    public static IReadOnlyList<string> PreferBridgeInterfacePaths(IEnumerable<string> devicePaths)
    {
        var list = devicePaths.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        if (list.Count == 0) return list;

        static bool HasMi02Col03(string p) =>
            p.Contains("&mi_02", StringComparison.OrdinalIgnoreCase)
            && p.Contains("col03", StringComparison.OrdinalIgnoreCase);

        static bool HasCol03(string p) =>
            p.Contains("col03", StringComparison.OrdinalIgnoreCase);

        var pool = list.Where(HasMi02Col03).ToList();
        if (pool.Count == 0)
            pool = list.Where(HasCol03).ToList();
        if (pool.Count == 0)
            pool = list;

        return pool
            .OrderBy(MiNumber)
            .ThenByDescending(HasCol03)
            .ThenBy(p => p, StringComparer.OrdinalIgnoreCase)
            .GroupBy(PhysicalDeviceKey, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Stable key for a physical pad (Windows Container ID when available).</summary>
    public static string PhysicalDeviceKey(string devicePath)
    {
        if (DeviceContainerId.TryGet(devicePath, out var containerId))
            return "container:" + containerId.ToString("D");

        // Fallback when SetupAPI lookup fails: strip &mi_ / &col from the hardware-id segment.
        var path = devicePath ?? "";
        path = StripHardwareSuffix(path, "&col");
        path = StripHardwareSuffix(path, "&mi_");
        return path;
    }

    static string StripHardwareSuffix(string path, string marker)
    {
        var idx = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return path;

        var end = idx + marker.Length;
        while (end < path.Length && (char.IsDigit(path[end]) || path[end] == '_'))
            end++;

        return path[..idx] + path[end..];
    }

    static int MiNumber(string? devicePath)
    {
        if (string.IsNullOrEmpty(devicePath)) return int.MaxValue;
        var idx = devicePath.IndexOf("&mi_", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return int.MaxValue;
        var start = idx + 4;
        var end = start;
        while (end < devicePath.Length && char.IsDigit(devicePath[end]))
            end++;
        return int.TryParse(devicePath[start..end], out var n) ? n : int.MaxValue;
    }

    public static string DisplayNameForModel(string model) => model switch
    {
        "sc1" => "Steam Controller",
        "sc2" => "Steam Controller 2",
        _ => "Steam Controller"
    };

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
        foreach (var dev in EnumerateInstances())
        {
            if (TryOpen(dev))
                return true;
        }
        return false;
    }

    public bool Open(string devicePath)
    {
        Close();
        if (string.IsNullOrWhiteSpace(devicePath))
            return false;

        foreach (var dev in Enumerate())
        {
            if (!string.Equals(dev.DevicePath, devicePath, StringComparison.OrdinalIgnoreCase))
                continue;
            return TryOpen(dev);
        }

        // Path may be a physical key (without col) — open best matching collection.
        var physical = PhysicalDeviceKey(devicePath);
        foreach (var dev in EnumerateInstances())
        {
            if (string.Equals(PhysicalDeviceKey(dev.DevicePath!), physical, StringComparison.OrdinalIgnoreCase))
                return TryOpen(dev);
        }
        return false;
    }

    public bool TryOpen(HidDevice device) => TryOpenDevice(device);

    bool TryOpenDevice(HidDevice device)
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
        StopRumbleLoop();
        try { WriteRumbleMotors(0, 0); } catch { /* ignore */ }
        try { _stream?.Dispose(); } catch { /* ignore */ }
        _stream = null;
        _device = null;
    }

    public bool DisableLizardMode()
    {
        if (_stream is null) return false;
        bool ok = SendCommand(CmdClearDigitalMappings, ReadOnlySpan<byte>.Empty);

        // Trackpads to NONE so firmware stops mouse emulation.
        Span<byte> trackpads = stackalloc byte[6];
        trackpads[0] = SettingLeftTrackpadMode;
        trackpads[1] = TrackpadNone;
        trackpads[2] = 0;
        trackpads[3] = SettingRightTrackpadMode;
        trackpads[4] = TrackpadNone;
        trackpads[5] = 0;
        ok &= SendCommand(CmdSetSettings, trackpads);
        if (!ok) return false;

        if (IsSc2)
        {
            // Disable Steam-presence watchdog that re-enables lizard; enable IMU.
            Span<byte> sc2 = stackalloc byte[6];
            sc2[0] = SettingSteamWatchdogEnable;
            sc2[1] = 0;
            sc2[2] = 0;
            sc2[3] = SettingImuMode;
            sc2[4] = (byte)(ImuModeFull & 0xFF);
            sc2[5] = (byte)(ImuModeFull >> 8);
            ok &= SendCommand(CmdSetSettings, sc2);
            return ok;
        }

        // SC1: gyro/accel are off until SETTING_IMU_MODE is set. Also bump wireless
        // packet version so dongle reports include the IMU fields.
        Span<byte> sc1 = stackalloc byte[6];
        sc1[0] = SettingWirelessPacketVersion;
        sc1[1] = 2;
        sc1[2] = 0;
        sc1[3] = SettingImuMode;
        sc1[4] = (byte)(ImuModeFull & 0xFF); // orient|accel|gyro
        sc1[5] = (byte)(ImuModeFull >> 8);
        if (!SendCommand(CmdSetSettings, sc1))
        {
            // Older wired firmware may reject wireless packet version — still try IMU alone.
            Span<byte> imuOnly = stackalloc byte[3];
            imuOnly[0] = SettingImuMode;
            imuOnly[1] = (byte)(ImuModeFull & 0xFF);
            imuOnly[2] = 0;
            _ = SendCommand(CmdSetSettings, imuOnly);
        }
        return true;
    }

    public bool EnableLizardMode()
    {
        if (_stream is null) return false;
        bool ok = SendCommand(CmdSetDefaultMappings, ReadOnlySpan<byte>.Empty);
        ok &= SendCommand(CmdLoadDefaultSettings, ReadOnlySpan<byte>.Empty);
        return ok;
    }

    public bool SendKeepalive() => DisableLizardMode();

    /// <summary>
    /// Pulse both motors briefly so the user can identify which pad is which.
    /// SC1 emulates rumble via trackpad haptic pulses; SC2 uses output report 0x80.
    /// </summary>
    public async Task<bool> IdentifyAsync(CancellationToken ct = default)
    {
        if (_stream is null) return false;

        // Snapshot game rumble so Identify doesn't leave motors stuck off afterward.
        byte prevL, prevR;
        lock (_rumbleGate)
        {
            prevL = _rumbleLeft;
            prevR = _rumbleRight;
        }

        const int durationMs = 450;
        SetRumble(0xC0, 0xC0);
        try
        {
            await Task.Delay(durationMs, ct);
        }
        catch (OperationCanceledException)
        {
            // fall through to restore
        }

        SetRumble(prevL, prevR);
        return true;
    }

    /// <summary>
    /// Xbox-style continuous rumble (0–255 per motor). SC2 output reports expire unless resent.
    /// </summary>
    public void SetRumble(byte leftMotor, byte rightMotor)
    {
        if (_stream is null) return;

        lock (_rumbleGate)
        {
            _rumbleLeft = leftMotor;
            _rumbleRight = rightMotor;
            WriteRumbleMotors(leftMotor, rightMotor);

            if (leftMotor == 0 && rightMotor == 0)
            {
                StopRumbleLoopUnlocked();
                return;
            }

            // SC2 haptics need ~40ms refresh; SC1 haptic pulses are finite so refresh
            // both to keep continuous rumble / identify buzz going.
            if (_rumbleTask is null || _rumbleTask.IsCompleted)
                StartRumbleLoopUnlocked();
        }
    }

    void StartRumbleLoopUnlocked()
    {
        StopRumbleLoopUnlocked();
        _rumbleCts = new CancellationTokenSource();
        var ct = _rumbleCts.Token;
        // SC1 pulses are short — refresh a bit faster so the buzz doesn't stutter.
        int periodMs = IsSc2 ? 40 : 25;
        _rumbleTask = Task.Run(async () =>
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(periodMs, ct).ConfigureAwait(false);
                    byte l, r;
                    lock (_rumbleGate)
                    {
                        l = _rumbleLeft;
                        r = _rumbleRight;
                        if (l == 0 && r == 0)
                            break;
                    }
                    WriteRumbleMotors(l, r);
                }
            }
            catch (OperationCanceledException) { /* stop */ }
        }, CancellationToken.None);
    }

    void StopRumbleLoop()
    {
        lock (_rumbleGate)
            StopRumbleLoopUnlocked();
    }

    void StopRumbleLoopUnlocked()
    {
        try { _rumbleCts?.Cancel(); } catch { /* ignore */ }
        try { _rumbleCts?.Dispose(); } catch { /* ignore */ }
        _rumbleCts = null;
        _rumbleTask = null;
    }

    void WriteRumbleMotors(byte leftMotor, byte rightMotor)
    {
        if (IsSc2)
        {
            // Xbox motors are 0–255; SC2 speeds are 16-bit.
            ushort left = (ushort)(leftMotor * 257);
            ushort right = (ushort)(rightMotor * 257);
            WriteTritonRumble(left, right);
        }
        else
        {
            // Classic SC1 has no rumble motors — Steam emulates rumble with trackpad haptics.
            WriteClassicHapticRumble(leftMotor, rightMotor);
        }
    }

    bool IsSc2 => Sc2ProductIds.Contains(ProductId);

    /// <summary>
    /// SC1: map Xbox motors onto left/right trackpad haptic pulse trains (cmd 0x8F).
    /// </summary>
    bool WriteClassicHapticRumble(byte leftMotor, byte rightMotor)
    {
        if (leftMotor == 0 && rightMotor == 0)
            return true;

        bool ok = true;
        if (leftMotor > 0)
            ok &= SendHapticPulse(HapticPadLeft, leftMotor);
        if (rightMotor > 0)
            ok &= SendHapticPulse(HapticPadRight, rightMotor);
        return ok;
    }

    bool SendHapticPulse(byte pad, byte motor)
    {
        // Left and right are swapped on this report for legacy reasons (hid-steam).
        byte wirePad = pad < HapticPadBoth ? (byte)(pad ^ 1) : pad;

        // Duration/interval are microseconds (max ~65ms). Shape a short buzz that the
        // rumble refresh loop re-fires so continuous game rumble feels sustained.
        // Keep SC1 quieter than a full-gain Steam buzz — trackpad haptics are loud.
        ushort duration = (ushort)(800 + motor * 10);    // ~0.8–3.4 ms on-time
        ushort interval = (ushort)(duration + 1200);
        ushort count = (ushort)Math.Clamp(6 + motor / 12, 6, 24);
        byte gain = 0; // 0 dB — positive gain is piercingly loud on SC1

        return SendHapticPulseRaw(wirePad, duration, interval, count, gain);
    }

    /// <summary>Steam-style mouse tick on one trackpad (SC1/SC2 pad click, not motor buzz).</summary>
    public void PulseMouseHaptic(bool rightPad, byte intensity)
    {
        if (intensity == 0) return;

        if (IsSc2)
        {
            // SC2 needs Triton output reports — classic 0x8F is barely felt / ignored.
            // Ascending strength: Low < Medium < High (tick gain, then pulse on High only).
            byte side = rightPad ? TritonSideRight : TritonSideLeft;
            if (intensity < 110)
            {
                WriteTritonHapticCommand(side, TritonHapticTick, gainDb: -4);
            }
            else if (intensity < 170)
            {
                WriteTritonHapticCommand(side, TritonHapticTick, gainDb: 0);
            }
            else
            {
                WriteTritonHapticCommand(side, TritonHapticTick, gainDb: 2);
                WriteTritonHapticPulse(side, onUs: 2400, offUs: 400, repeat: 1);
            }
            return;
        }

        byte pad = rightPad ? HapticPadRight : HapticPadLeft;
        byte wirePad = (byte)(pad ^ 1);

        // Ascending: Low < Medium < High.
        ushort duration;
        sbyte gainDbSc1;
        if (intensity < 110)
        {
            duration = 1200;
            gainDbSc1 = -4;
        }
        else if (intensity < 170)
        {
            duration = 1800;
            gainDbSc1 = 0;
        }
        else
        {
            duration = 2400;
            gainDbSc1 = 2;
        }

        ushort interval = (ushort)(duration + 300);
        SendHapticPulseRaw(wirePad, duration, interval, count: 1, (byte)gainDbSc1);
    }

    bool WriteTritonHapticCommand(byte side, byte command, sbyte gainDb)
    {
        if (_stream is null) return false;
        var buffer = new byte[64];
        buffer[0] = OutReportHapticCommand;
        buffer[1] = side;
        buffer[2] = command;
        buffer[3] = (byte)gainDb;
        return WriteOutputReport(buffer);
    }

    bool WriteTritonHapticPulse(byte side, ushort onUs, ushort offUs, ushort repeat)
    {
        if (_stream is null) return false;
        var buffer = new byte[64];
        buffer[0] = OutReportHapticPulse;
        buffer[1] = side;
        buffer[2] = (byte)(onUs & 0xFF);
        buffer[3] = (byte)(onUs >> 8);
        buffer[4] = (byte)(offUs & 0xFF);
        buffer[5] = (byte)(offUs >> 8);
        buffer[6] = (byte)(repeat & 0xFF);
        buffer[7] = (byte)(repeat >> 8);
        return WriteOutputReport(buffer);
    }

    bool WriteOutputReport(byte[] buffer)
    {
        lock (_writeLock)
        {
            try
            {
                _stream!.Write(buffer, 0, buffer.Length);
                return true;
            }
            catch
            {
                try
                {
                    _stream!.Write(buffer, 0, 10);
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }
    }

    bool SendHapticPulseRaw(byte wirePad, ushort duration, ushort interval, ushort count, byte gain)
    {
        Span<byte> payload = stackalloc byte[8];
        payload[0] = wirePad;
        payload[1] = (byte)(duration & 0xFF);
        payload[2] = (byte)(duration >> 8);
        payload[3] = (byte)(interval & 0xFF);
        payload[4] = (byte)(interval >> 8);
        payload[5] = (byte)(count & 0xFF);
        payload[6] = (byte)(count >> 8);
        payload[7] = gain;
        return SendCommand(CmdTriggerHapticPulse, payload);
    }

    /// <summary>SC2 / Triton haptic rumble output report (SDL ID_OUT_REPORT_HAPTIC_RUMBLE).</summary>
    bool WriteTritonRumble(ushort leftSpeed, ushort rightSpeed)
    {
        if (_stream is null) return false;

        // Report is 10 bytes of content; HID write often expects a full 64-byte buffer.
        var buffer = new byte[64];
        buffer[0] = OutReportHapticRumble; // report id
        buffer[1] = 0; // type
        buffer[2] = 0; // intensity lo
        buffer[3] = 0; // intensity hi
        buffer[4] = (byte)(leftSpeed & 0xFF);
        buffer[5] = (byte)(leftSpeed >> 8);
        buffer[6] = 0; // left gain
        buffer[7] = (byte)(rightSpeed & 0xFF);
        buffer[8] = (byte)(rightSpeed >> 8);
        buffer[9] = 0; // right gain

        return WriteOutputReport(buffer);
    }

    public bool TryReadState(out MistMapper.Shared.SteamControllerState state, int timeoutMs = 50)
    {
        state = new();
        if (_stream is null) return false;

        try
        {
            _stream.ReadTimeout = timeoutMs;
            int read = _stream.Read(_readBuffer, 0, _readBuffer.Length);
            if (read <= 0) return false;
            var span = _readBuffer.AsSpan(0, read);
            return IsSc2
                ? SteamReportParser.TryParse(span, out state)
                : SteamClassicReportParser.TryParse(span, out state);
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
