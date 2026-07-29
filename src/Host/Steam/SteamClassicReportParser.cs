using System.Buffers.Binary;
using MistMapper.Shared;

namespace MistMapper.Host.Steam;

/// <summary>
/// Parses original Steam Controller (2015) interrupt packets.
/// Layout matches Linux <c>hid-steam.c</c> <c>steam_do_input_event</c>:
/// bytes 0–1 = <c>0x01 0x00</c>, byte 2 = subtype (<c>0x01</c> = input), payload from byte 4.
/// </summary>
public static class SteamClassicReportParser
{
    public const byte ReportId = 0x01;
    public const byte SubtypeInput = 0x01;

    public static bool IsInputReport(ReadOnlySpan<byte> buf)
    {
        buf = StripHidReportId(buf);
        return buf.Length >= 24
            && buf[0] == ReportId
            && buf[1] == 0x00
            && buf[2] == SubtypeInput;
    }

    public static bool TryParse(ReadOnlySpan<byte> buf, out SteamControllerState state)
    {
        state = new SteamControllerState();
        buf = StripHidReportId(buf);
        if (buf.Length < 24 || buf[0] != ReportId || buf[1] != 0x00 || buf[2] != SubtypeInput)
            return false;

        state.Sequence = buf[4]; // low byte of u32 sequence is enough for freshness

        byte b8 = buf[8], b9 = buf[9], b10 = buf[10];

        // Byte 8
        state.Rb = (b8 & 0x04) != 0;
        state.Lb = (b8 & 0x08) != 0;
        state.Y = (b8 & 0x10) != 0;
        state.B = (b8 & 0x20) != 0;
        state.X = (b8 & 0x40) != 0;
        state.A = (b8 & 0x80) != 0;

        // Byte 9
        state.DpadUp = (b9 & 0x01) != 0;
        state.DpadRight = (b9 & 0x02) != 0;
        state.DpadLeft = (b9 & 0x04) != 0;
        state.DpadDown = (b9 & 0x08) != 0;
        state.View = (b9 & 0x10) != 0;
        state.Steam = (b9 & 0x20) != 0;
        state.Menu = (b9 & 0x40) != 0;
        state.L4 = (b9 & 0x80) != 0; // left grip (SC1 has one grip per side)

        // Byte 10
        state.R4 = (b10 & 0x01) != 0; // right grip
        state.LeftTrackpadClick = (b10 & 0x02) != 0;
        state.RightTrackpadClick = (b10 & 0x04) != 0;
        bool lpadTouched = (b10 & 0x08) != 0;
        state.RightTrackpadTouch = (b10 & 0x10) != 0;
        state.LsClick = (b10 & 0x40) != 0;
        bool lpadAndJoy = (b10 & 0x80) != 0;

        state.LeftTrackpadTouch = lpadTouched || lpadAndJoy;
        // No capacitive stick touch bit on SC1; treat click presence as not touch.
        state.LeftStickTouch = false;
        state.RightStickTouch = false;
        state.RsClick = false;
        state.L5 = false;
        state.R5 = false;

        // u8 triggers → same ushort scale SC2 ToFrame expects (/32767).
        state.LeftTrigger = (ushort)(buf[11] * 257);
        state.RightTrigger = (ushort)(buf[12] * 257);

        short sharedX = BinaryPrimitives.ReadInt16LittleEndian(buf[16..]);
        short sharedY = BinaryPrimitives.ReadInt16LittleEndian(buf[18..]);

        // Stick vs left pad share axes 16–19 (same routing as hid-steam).
        if (lpadTouched)
        {
            state.LeftTrackpadX = sharedX;
            state.LeftTrackpadY = sharedY;
            if (lpadAndJoy && buf.Length >= 58)
            {
                state.LeftStickX = BinaryPrimitives.ReadInt16LittleEndian(buf[54..]);
                state.LeftStickY = BinaryPrimitives.ReadInt16LittleEndian(buf[56..]);
            }
            else if (!lpadAndJoy)
            {
                state.LeftStickX = 0;
                state.LeftStickY = 0;
            }
        }
        else
        {
            state.LeftStickX = sharedX;
            state.LeftStickY = sharedY;
            state.LeftTrackpadX = 0;
            state.LeftTrackpadY = 0;
        }

        // Wired reports often include dedicated left-pad abs at 58–61.
        if (state.LeftTrackpadTouch && buf.Length >= 62)
        {
            short padX = BinaryPrimitives.ReadInt16LittleEndian(buf[58..]);
            short padY = BinaryPrimitives.ReadInt16LittleEndian(buf[60..]);
            if (padX != 0 || padY != 0)
            {
                state.LeftTrackpadX = padX;
                state.LeftTrackpadY = padY;
            }
        }

        state.RightTrackpadX = BinaryPrimitives.ReadInt16LittleEndian(buf[20..]);
        state.RightTrackpadY = BinaryPrimitives.ReadInt16LittleEndian(buf[22..]);

        // No physical right stick on SC1.
        state.RightStickX = 0;
        state.RightStickY = 0;

        if (buf.Length >= 40)
        {
            // Gyro/accel are present in the report layout whenever IMU mode is on;
            // do not require non-zero samples (rest pose is ~0).
            state.HasImu = true;
            state.GyroX = BinaryPrimitives.ReadInt16LittleEndian(buf[34..]);
            state.GyroY = BinaryPrimitives.ReadInt16LittleEndian(buf[36..]);
            state.GyroZ = BinaryPrimitives.ReadInt16LittleEndian(buf[38..]);
        }

        return true;
    }

    /// <summary>
    /// Windows/HidSharp often prefixes Valve's 0x01 packet with a HID report-id byte (0x00).
    /// </summary>
    public static ReadOnlySpan<byte> StripHidReportId(ReadOnlySpan<byte> buf)
    {
        if (buf.Length >= 25
            && buf[0] == 0x00
            && buf[1] == ReportId
            && buf[2] == 0x00
            && buf[3] == SubtypeInput)
            return buf[1..];
        return buf;
    }
}
