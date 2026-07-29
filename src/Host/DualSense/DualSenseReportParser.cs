using System.Buffers.Binary;

namespace MistMapper.Host.DualSense;

/// <summary>
/// Parses DualSense / DualSense Edge full HID state reports (USB <c>0x01</c>, BT <c>0x31</c>).
/// Payload layout matches SDL <c>PS5StatePacket_t</c> / Linux <c>dualsense_input_report</c>.
/// </summary>
public static class DualSenseReportParser
{
    public const byte UsbReportId = 0x01;
    public const byte BluetoothReportId = 0x31;
    public const int TouchpadMaxX = 1920;
    public const int TouchpadMaxY = 1070;

    public static bool TryParse(ReadOnlySpan<byte> buf, out DualSenseState state, bool requireBtCrc = true)
    {
        state = new DualSenseState();
        if (buf.Length < 10) return false;

        ReadOnlySpan<byte> payload;
        if (buf[0] == UsbReportId)
        {
            payload = buf[1..];
        }
        else if (buf[0] == BluetoothReportId)
        {
            if (buf.Length < 14) return false;
            if (requireBtCrc && !DualSenseCrc32.VerifyBluetoothInput(buf))
                return false;
            payload = buf[2..];
        }
        else
        {
            return false;
        }

        if (payload.Length < 9)
            return false;

        return TryParsePayload(payload, state);
    }

    public static bool TryParsePayload(ReadOnlySpan<byte> p, DualSenseState state)
    {
        if (p.Length < 9) return false;

        state.LeftStickX = p[0];
        state.LeftStickY = p[1];
        state.RightStickX = p[2];
        state.RightStickY = p[3];
        state.LeftTrigger = p[4];
        state.RightTrigger = p[5];
        state.Sequence = p.Length > 6 ? p[6] : (byte)0;

        byte b0 = p[7];
        ApplyHat((byte)(b0 & 0x0F), state);
        state.Square = (b0 & 0x10) != 0;
        state.Cross = (b0 & 0x20) != 0;
        state.Circle = (b0 & 0x40) != 0;
        state.Triangle = (b0 & 0x80) != 0;

        if (p.Length > 8)
        {
            byte b1 = p[8];
            state.L1 = (b1 & 0x01) != 0;
            state.R1 = (b1 & 0x02) != 0;
            state.Create = (b1 & 0x10) != 0;
            state.Options = (b1 & 0x20) != 0;
            state.L3 = (b1 & 0x40) != 0;
            state.R3 = (b1 & 0x80) != 0;
        }

        if (p.Length > 9)
        {
            byte b2 = p[9];
            state.Ps = (b2 & 0x01) != 0;
            state.TouchpadClick = (b2 & 0x02) != 0;
            state.Mute = (b2 & 0x04) != 0;
            state.LeftPaddle = (b2 & 0x40) != 0;
            state.RightPaddle = (b2 & 0x80) != 0;
        }

        if (p.Length >= 27)
        {
            state.GyroX = BinaryPrimitives.ReadInt16LittleEndian(p[15..]);
            state.GyroY = BinaryPrimitives.ReadInt16LittleEndian(p[17..]);
            state.GyroZ = BinaryPrimitives.ReadInt16LittleEndian(p[19..]);
            state.HasImu = true;
        }

        // Standard touch block at offset 32 (PS5StatePacket_t).
        if (p.Length >= 36)
            ApplyTouch(p, 32, state);

        return true;
    }

    static void ApplyTouch(ReadOnlySpan<byte> p, int counterOffset, DualSenseState state)
    {
        if (p.Length < counterOffset + 4) return;

        byte counter = p[counterOffset];
        // High bit clear = finger down.
        bool active = (counter & 0x80) == 0;
        state.TouchpadTouch = active;
        if (!active)
        {
            state.TouchpadX = 0;
            state.TouchpadY = 0;
            return;
        }

        int rawX = p[counterOffset + 1] | ((p[counterOffset + 2] & 0x0F) << 8);
        int rawY = (p[counterOffset + 2] >> 4) | (p[counterOffset + 3] << 4);
        // Normalize to signed short range centered at pad middle (matches SC trackpad feel).
        float nx = (rawX / (float)TouchpadMaxX) * 2f - 1f;
        // Negate Y so it matches the Steam Controller convention (Y-up).
        // The DualSense touchpad raw Y increases downward (screen coords).
        float ny = -((rawY / (float)TouchpadMaxY) * 2f - 1f);
        state.TouchpadX = (short)Math.Clamp((int)(nx * 32767f), short.MinValue, short.MaxValue);
        state.TouchpadY = (short)Math.Clamp((int)(ny * 32767f), short.MinValue, short.MaxValue);
    }

    static void ApplyHat(byte hat, DualSenseState state)
    {
        state.DpadUp = hat is 0 or 1 or 7;
        state.DpadRight = hat is 1 or 2 or 3;
        state.DpadDown = hat is 3 or 4 or 5;
        state.DpadLeft = hat is 5 or 6 or 7;
    }
}
