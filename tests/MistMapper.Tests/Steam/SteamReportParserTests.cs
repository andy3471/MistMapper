using System.Buffers.Binary;
using MistMapper.Host.Steam;

namespace MistMapper.Tests.Steam;

public sealed class SteamReportParserTests
{
    [Theory]
    [InlineData(SteamReportParser.ReportState)]
    [InlineData(SteamReportParser.ReportStateLegacy)]
    public void IsStateReport_accepts_known_report_ids(byte reportId)
    {
        SteamReportParser.IsStateReport(reportId).Should().BeTrue();
    }

    [Theory]
    [InlineData(0x00)]
    [InlineData(0x01)]
    public void IsStateReport_rejects_other_ids(byte reportId)
    {
        SteamReportParser.IsStateReport(reportId).Should().BeFalse();
    }

    [Fact]
    public void TryParse_rejects_short_buffer()
    {
        var ok = SteamReportParser.TryParse([SteamReportParser.ReportState], out _);
        ok.Should().BeFalse();
    }

    [Fact]
    public void TryParse_rejects_unknown_report_id()
    {
        var buf = new byte[30];
        buf[0] = 0x99;

        SteamReportParser.TryParse(buf, out _).Should().BeFalse();
    }

    [Fact]
    public void TryParse_reads_face_buttons_and_sequence()
    {
        var buf = CreateReport();
        buf[1] = 42;
        buf[2] = 0x01; // A
        buf[3] = 0x20; // DpadUp

        var ok = SteamReportParser.TryParse(buf, out var state);

        ok.Should().BeTrue();
        state.Sequence.Should().Be(42);
        state.A.Should().BeTrue();
        state.DpadUp.Should().BeTrue();
        state.B.Should().BeFalse();
    }

    [Fact]
    public void TryParse_reads_analog_values()
    {
        var buf = CreateReport();
        BinaryPrimitives.WriteInt16LittleEndian(buf.AsSpan(6), 1000);
        BinaryPrimitives.WriteInt16LittleEndian(buf.AsSpan(8), 2000);
        BinaryPrimitives.WriteInt16LittleEndian(buf.AsSpan(10), -5000);
        BinaryPrimitives.WriteInt16LittleEndian(buf.AsSpan(12), 3000);

        SteamReportParser.TryParse(buf, out var state).Should().BeTrue();
        state.LeftTrigger.Should().Be(1000);
        state.RightTrigger.Should().Be(2000);
        state.LeftStickX.Should().Be(-5000);
        state.LeftStickY.Should().Be(3000);
    }

    [Fact]
    public void TryParse_reads_imu_when_buffer_is_long_enough()
    {
        var buf = CreateReport(length: 46);
        BinaryPrimitives.WriteInt16LittleEndian(buf.AsSpan(40), 11);
        BinaryPrimitives.WriteInt16LittleEndian(buf.AsSpan(42), 22);
        BinaryPrimitives.WriteInt16LittleEndian(buf.AsSpan(44), 33);

        SteamReportParser.TryParse(buf, out var state).Should().BeTrue();
        state.HasImu.Should().BeTrue();
        state.GyroX.Should().Be(11);
        state.GyroY.Should().Be(22);
        state.GyroZ.Should().Be(33);
    }

    [Fact]
    public void TryParse_omits_imu_for_short_reports()
    {
        SteamReportParser.TryParse(CreateReport(length: 30), out var state).Should().BeTrue();
        state.HasImu.Should().BeFalse();
    }

    static byte[] CreateReport(byte reportId = SteamReportParser.ReportState, int length = 30)
    {
        var buf = new byte[length];
        buf[0] = reportId;
        return buf;
    }
}
