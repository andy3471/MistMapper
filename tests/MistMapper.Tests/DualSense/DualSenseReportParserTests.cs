using System.Buffers.Binary;
using MistMapper.Host.Drivers;
using MistMapper.Host.DualSense;
using MistMapper.Shared;

namespace MistMapper.Tests.DualSense;

public sealed class DualSenseReportParserTests
{
    [Fact]
    public void TryParse_usb_reads_face_buttons_and_sticks()
    {
        var buf = CreateUsbReport();
        buf[1 + 7] = (byte)(0x08 | 0x20); // hat centered + Cross
        buf[1 + 0] = 200;
        buf[1 + 4] = 255;

        DualSenseReportParser.TryParse(buf, out var state).Should().BeTrue();
        state.Cross.Should().BeTrue();
        state.Circle.Should().BeFalse();
        state.LeftStickX.Should().Be(200);
        state.LeftTrigger.Should().Be(255);
    }

    [Fact]
    public void TryParse_usb_reads_shoulders_and_dpad()
    {
        var buf = CreateUsbReport();
        buf[1 + 7] = 0x02; // hat right
        buf[1 + 8] = 0x01 | 0x02;
        buf[1 + 9] = 0x01 | 0x02;

        DualSenseReportParser.TryParse(buf, out var state).Should().BeTrue();
        state.DpadRight.Should().BeTrue();
        state.L1.Should().BeTrue();
        state.R1.Should().BeTrue();
        state.Ps.Should().BeTrue();
        state.TouchpadClick.Should().BeTrue();
    }

    [Fact]
    public void TryParse_usb_reads_gyro_and_touch()
    {
        var buf = CreateUsbReport(length: 64);
        BinaryPrimitives.WriteInt16LittleEndian(buf.AsSpan(1 + 15), 111);
        BinaryPrimitives.WriteInt16LittleEndian(buf.AsSpan(1 + 17), 222);
        BinaryPrimitives.WriteInt16LittleEndian(buf.AsSpan(1 + 19), 333);
        buf[1 + 32] = 0x01; // finger down
        buf[1 + 33] = 0x00;
        buf[1 + 34] = 0x80; // X=2048 would be out of range; use mid
        buf[1 + 35] = 0x00;

        DualSenseReportParser.TryParse(buf, out var state).Should().BeTrue();
        state.HasImu.Should().BeTrue();
        state.GyroX.Should().Be(111);
        state.GyroY.Should().Be(222);
        state.GyroZ.Should().Be(333);
        state.TouchpadTouch.Should().BeTrue();
    }

    [Fact]
    public void TryParse_bt_accepts_valid_crc_and_rejects_bad()
    {
        var buf = CreateBtReport();
        DualSenseReportParser.TryParse(buf, out var okState).Should().BeTrue();
        okState.Cross.Should().BeTrue();

        buf[^1] ^= 0xFF;
        DualSenseReportParser.TryParse(buf, out _).Should().BeFalse();
    }

    [Fact]
    public void TryParse_rejects_unknown_report_id()
    {
        var buf = new byte[64];
        buf[0] = 0x99;
        DualSenseReportParser.TryParse(buf, out _).Should().BeFalse();
    }

    [Fact]
    public void ToFrame_maps_sony_to_physical_ids()
    {
        var ds = new DualSenseState
        {
            Cross = true,
            Circle = true,
            Square = true,
            Triangle = true,
            L1 = true,
            Create = true,
            Ps = true,
            LeftTrigger = 128,
            RightTrigger = 255,
            TouchpadTouch = true,
            TouchpadClick = true,
            HasImu = true,
            GyroX = 50,
            GyroY = -50
        };

        var frame = DualSenseDriver.ToFrame(ds);
        frame.Digitals["A"].Should().BeTrue();
        frame.Digitals["B"].Should().BeTrue();
        frame.Digitals["X"].Should().BeTrue();
        frame.Digitals["Y"].Should().BeTrue();
        frame.Digitals["Lb"].Should().BeTrue();
        frame.Digitals["View"].Should().BeTrue();
        frame.Digitals["Steam"].Should().BeTrue();
        frame.Digitals["RightTrackpad"].Should().BeTrue();
        frame.Analogs["Lt"].Should().BeApproximately(128 / 255f, 0.001f);
        frame.Analogs["Rt"].Should().BeApproximately(1f, 0.001f);
        frame.Vectors.Should().ContainKey("Gyro");
    }

    [Fact]
    public void GetCapabilities_dualsense_returns_ds_caps()
    {
        var registry = new DriverRegistry();
        var caps = registry.GetCapabilities(DriverIds.DualSense);
        caps.DriverId.Should().Be(DriverIds.DualSense);
        caps.SupportsTrackpadModes.Should().BeTrue();
        caps.SupportsGyroModes.Should().BeTrue();
        caps.Inputs.Should().Contain(i => i.Id == "RightTrackpad");
        caps.Inputs.Should().NotContain(i => i.Id == "LeftTrackpad");
    }

    static byte[] CreateUsbReport(int length = 48)
    {
        var buf = new byte[length];
        buf[0] = DualSenseReportParser.UsbReportId;
        buf[1 + 7] = 0x08;
        return buf;
    }

    static byte[] CreateBtReport()
    {
        var buf = new byte[78];
        buf[0] = DualSenseReportParser.BluetoothReportId;
        buf[1] = 0x01;
        buf[2 + 7] = (byte)(0x08 | 0x20);
        DualSenseCrc32.WriteBluetoothInputCrc(buf);
        return buf;
    }
}
