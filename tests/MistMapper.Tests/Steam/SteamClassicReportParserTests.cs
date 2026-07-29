using System.Buffers.Binary;
using MistMapper.Host.Steam;

namespace MistMapper.Tests.Steam;

public sealed class SteamClassicReportParserTests
{
    [Fact]
    public void IsInputReport_accepts_classic_envelope()
    {
        SteamClassicReportParser.IsInputReport(CreateReport()).Should().BeTrue();
    }

    [Fact]
    public void TryParse_rejects_sc2_report_ids()
    {
        var buf = new byte[64];
        buf[0] = SteamReportParser.ReportState;
        SteamClassicReportParser.TryParse(buf, out _).Should().BeFalse();

        buf[0] = SteamReportParser.ReportStateLegacy;
        SteamClassicReportParser.TryParse(buf, out _).Should().BeFalse();
    }

    [Fact]
    public void TryParse_rejects_short_or_wrong_subtype()
    {
        SteamClassicReportParser.TryParse([0x01, 0x00, 0x01], out _).Should().BeFalse();

        var buf = CreateReport();
        buf[2] = 0x03; // connect event
        SteamClassicReportParser.TryParse(buf, out _).Should().BeFalse();
    }

    [Fact]
    public void TryParse_accepts_windows_leading_report_id_zero()
    {
        var inner = CreateReport();
        inner[8] = 0x80; // A
        var buf = new byte[inner.Length + 1];
        buf[0] = 0x00;
        inner.CopyTo(buf.AsSpan(1));

        SteamClassicReportParser.TryParse(buf, out var state).Should().BeTrue();
        state.A.Should().BeTrue();
    }

    [Fact]
    public void TryParse_reads_face_buttons_and_grips()
    {
        var buf = CreateReport();
        buf[8] = 0x80; // A
        buf[9] = 0x20 | 0x80; // Steam + left grip
        buf[10] = 0x01; // right grip

        SteamClassicReportParser.TryParse(buf, out var state).Should().BeTrue();
        state.A.Should().BeTrue();
        state.Steam.Should().BeTrue();
        state.L4.Should().BeTrue();
        state.R4.Should().BeTrue();
        state.L5.Should().BeFalse();
        state.R5.Should().BeFalse();
        state.RightStickX.Should().Be(0);
        state.RightStickY.Should().Be(0);
        state.RsClick.Should().BeFalse();
    }

    [Fact]
    public void TryParse_scales_u8_triggers()
    {
        var buf = CreateReport();
        buf[11] = 255;
        buf[12] = 128;

        SteamClassicReportParser.TryParse(buf, out var state).Should().BeTrue();
        state.LeftTrigger.Should().Be(255 * 257);
        state.RightTrigger.Should().Be(128 * 257);
    }

    [Fact]
    public void TryParse_routes_shared_axes_to_stick_when_pad_untouched()
    {
        var buf = CreateReport();
        BinaryPrimitives.WriteInt16LittleEndian(buf.AsSpan(16), 1000);
        BinaryPrimitives.WriteInt16LittleEndian(buf.AsSpan(18), -2000);

        SteamClassicReportParser.TryParse(buf, out var state).Should().BeTrue();
        state.LeftStickX.Should().Be(1000);
        state.LeftStickY.Should().Be(-2000);
        state.LeftTrackpadX.Should().Be(0);
        state.LeftTrackpadY.Should().Be(0);
        state.LeftTrackpadTouch.Should().BeFalse();
    }

    [Fact]
    public void TryParse_routes_shared_axes_to_left_pad_when_touched()
    {
        var buf = CreateReport();
        buf[10] = 0x08; // lpad touched
        BinaryPrimitives.WriteInt16LittleEndian(buf.AsSpan(16), 1111);
        BinaryPrimitives.WriteInt16LittleEndian(buf.AsSpan(18), 2222);

        SteamClassicReportParser.TryParse(buf, out var state).Should().BeTrue();
        state.LeftTrackpadTouch.Should().BeTrue();
        state.LeftTrackpadX.Should().Be(1111);
        state.LeftTrackpadY.Should().Be(2222);
        state.LeftStickX.Should().Be(0);
        state.LeftStickY.Should().Be(0);
    }

    [Fact]
    public void TryParse_reads_right_pad_and_clicks()
    {
        var buf = CreateReport();
        buf[10] = 0x04 | 0x10; // rpad click + touch
        BinaryPrimitives.WriteInt16LittleEndian(buf.AsSpan(20), -3000);
        BinaryPrimitives.WriteInt16LittleEndian(buf.AsSpan(22), 4000);

        SteamClassicReportParser.TryParse(buf, out var state).Should().BeTrue();
        state.RightTrackpadClick.Should().BeTrue();
        state.RightTrackpadTouch.Should().BeTrue();
        state.RightTrackpadX.Should().Be(-3000);
        state.RightTrackpadY.Should().Be(4000);
    }

    [Fact]
    public void TryParse_reads_gyro_when_present()
    {
        var buf = CreateReport(length: 64);
        BinaryPrimitives.WriteInt16LittleEndian(buf.AsSpan(34), 11);
        BinaryPrimitives.WriteInt16LittleEndian(buf.AsSpan(36), 22);
        BinaryPrimitives.WriteInt16LittleEndian(buf.AsSpan(38), 33);

        SteamClassicReportParser.TryParse(buf, out var state).Should().BeTrue();
        state.HasImu.Should().BeTrue();
        state.GyroX.Should().Be(11);
        state.GyroY.Should().Be(22);
        state.GyroZ.Should().Be(33);
    }

    [Fact]
    public void TryParse_marks_imu_even_when_gyro_is_at_rest()
    {
        var buf = CreateReport(length: 64);
        SteamClassicReportParser.TryParse(buf, out var state).Should().BeTrue();
        state.HasImu.Should().BeTrue();
        state.GyroX.Should().Be(0);
        state.GyroY.Should().Be(0);
        state.GyroZ.Should().Be(0);
    }

    [Fact]
    public void SteamReportParser_rejects_classic_reports()
    {
        SteamReportParser.TryParse(CreateReport(), out _).Should().BeFalse();
    }

    static byte[] CreateReport(int length = 64)
    {
        var buf = new byte[length];
        buf[0] = SteamClassicReportParser.ReportId;
        buf[1] = 0x00;
        buf[2] = SteamClassicReportParser.SubtypeInput;
        buf[3] = 60;
        return buf;
    }
}
