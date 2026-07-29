using MistMapper.Host.Steam;

namespace MistMapper.Tests.Steam;

public sealed class SteamControllerEnumerateTests
{
    const string Mi02Col03 =
        @"\\?\hid#vid_28de&pid_1304&mi_02&col03#8&23daf111&0&0002#{4d1e55b2-f16f-11cf-88cb-001111000030}";
    const string Mi03Col03 =
        @"\\?\hid#vid_28de&pid_1304&mi_03&col03#8&370b0d4f&0&0002#{4d1e55b2-f16f-11cf-88cb-001111000030}";
    const string Mi06 =
        @"\\?\hid#vid_28de&pid_1304&mi_06#8&35009802&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}";
    const string SecondPadMi02 =
        @"\\?\hid#vid_28de&pid_1304&mi_02&col03#8&aaaaaaaa&0&0002#{4d1e55b2-f16f-11cf-88cb-001111000030}";

    [Fact]
    public void PreferBridgeInterfacePaths_drops_mi06_when_col03_exists()
    {
        var preferred = SteamControllerDevice.PreferBridgeInterfacePaths([Mi02Col03, Mi03Col03, Mi06]);

        preferred.Should().NotContain(Mi06);
        preferred.Should().NotContain(Mi03Col03);
        preferred.Should().ContainSingle().Which.Should().Be(Mi02Col03);
    }

    [Fact]
    public void PreferBridgeInterfacePaths_keeps_two_physical_pads()
    {
        var preferred = SteamControllerDevice.PreferBridgeInterfacePaths([Mi02Col03, SecondPadMi02, Mi06, Mi03Col03]);

        preferred.Should().HaveCount(2);
        preferred.Should().Contain(Mi02Col03);
        preferred.Should().Contain(SecondPadMi02);
        preferred.Should().NotContain(Mi06);
        preferred.Should().NotContain(Mi03Col03);
    }

    [Fact]
    public void PhysicalDeviceKey_strips_mi_and_col()
    {
        var key = SteamControllerDevice.PhysicalDeviceKey(Mi02Col03);
        // Fake paths won't resolve a Container ID; fallback strips interface tokens.
        if (!key.StartsWith("container:", StringComparison.OrdinalIgnoreCase))
        {
            key.Should().NotContain("&mi_");
            key.Should().NotContain("&col");
            key.Should().Contain("vid_28de");
            key.Should().Contain("pid_1304");
        }
    }

    [Fact]
    public void ToDeviceInstanceId_converts_hid_path()
    {
        var id = DeviceContainerId.ToDeviceInstanceId(Mi02Col03);
        id.Should().StartWith(@"HID\");
        id.Should().Contain(@"VID_28DE&PID_1304&MI_02&COL03");
        id.Should().Contain(@"8&23DAF111&0&0002");
        id.Should().NotContain("{");
    }
}
