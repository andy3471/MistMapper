using System.Buffers.Binary;
using MistMapper.Shared;

namespace MistMapper.Host.Steam;

/// <summary>Parses Steam Controller 2026 vendor HID state reports (0x42 / 0x45).</summary>
public static class SteamReportParser
{
    public const byte ReportState = 0x45;
    public const byte ReportStateLegacy = 0x42;

    public static bool IsStateReport(byte reportId) =>
        reportId is ReportState or ReportStateLegacy;

    public static bool TryParse(ReadOnlySpan<byte> buf, out SteamControllerState state)
    {
        state = new SteamControllerState();
        if (buf.Length < 30 || !IsStateReport(buf[0]))
            return false;

        state.Sequence = buf[1];

        byte b0 = buf[2], b1 = buf[3], b2 = buf[4], b3 = buf[5];

        state.A = (b0 & 0x01) != 0;
        state.B = (b0 & 0x02) != 0;
        state.X = (b0 & 0x04) != 0;
        state.Y = (b0 & 0x08) != 0;
        state.RsClick = (b0 & 0x20) != 0;
        state.Menu = (b0 & 0x40) != 0;
        state.R4 = (b0 & 0x80) != 0;

        state.R5 = (b1 & 0x01) != 0;
        state.Rb = (b1 & 0x02) != 0;
        state.DpadDown = (b1 & 0x04) != 0;
        state.DpadRight = (b1 & 0x08) != 0;
        state.DpadLeft = (b1 & 0x10) != 0;
        state.DpadUp = (b1 & 0x20) != 0;
        state.View = (b1 & 0x40) != 0;
        state.LsClick = (b1 & 0x80) != 0;

        state.Steam = (b2 & 0x01) != 0;
        state.L4 = (b2 & 0x02) != 0;
        state.L5 = (b2 & 0x04) != 0;
        state.Lb = (b2 & 0x08) != 0;
        // SDL TritonButtons: bit4 = RStick capacitive touch, bit5 = RPad touch, bit6 = RPad click.
        state.RightStickTouch = (b2 & 0x10) != 0;
        state.RightTrackpadTouch = (b2 & 0x20) != 0;
        state.RightTrackpadClick = (b2 & 0x40) != 0;

        // SDL TritonButtons byte5: bit0 = LStick touch, bit1 = LPad touch, bit2 = LPad click.
        state.LeftStickTouch = (b3 & 0x01) != 0;
        state.LeftTrackpadTouch = (b3 & 0x02) != 0;
        state.LeftTrackpadClick = (b3 & 0x04) != 0;

        // Triggers are unsigned (u16). Reading as i16 made full-pull values
        // (>32767) look negative, which ScaleAnalog then clamped to 0 — half-press
        // worked, bottoming-out released.
        state.LeftTrigger = BinaryPrimitives.ReadUInt16LittleEndian(buf[6..]);
        state.RightTrigger = BinaryPrimitives.ReadUInt16LittleEndian(buf[8..]);
        state.LeftStickX = BinaryPrimitives.ReadInt16LittleEndian(buf[10..]);
        state.LeftStickY = BinaryPrimitives.ReadInt16LittleEndian(buf[12..]);
        state.RightStickX = BinaryPrimitives.ReadInt16LittleEndian(buf[14..]);
        state.RightStickY = BinaryPrimitives.ReadInt16LittleEndian(buf[16..]);
        state.LeftTrackpadX = BinaryPrimitives.ReadInt16LittleEndian(buf[18..]);
        state.LeftTrackpadY = BinaryPrimitives.ReadInt16LittleEndian(buf[20..]);
        state.RightTrackpadX = BinaryPrimitives.ReadInt16LittleEndian(buf[24..]);
        state.RightTrackpadY = BinaryPrimitives.ReadInt16LittleEndian(buf[26..]);

        if (buf.Length >= 46)
        {
            state.HasImu = true;
            state.GyroX = BinaryPrimitives.ReadInt16LittleEndian(buf[40..]);
            state.GyroY = BinaryPrimitives.ReadInt16LittleEndian(buf[42..]);
            state.GyroZ = BinaryPrimitives.ReadInt16LittleEndian(buf[44..]);
        }

        return true;
    }
}
