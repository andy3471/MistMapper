using MistMapper.Host.Drivers;
using MistMapper.Shared;

namespace MistMapper.Tests.Drivers;

public sealed class SteamControllerCapabilitiesTests
{
    [Fact]
    public void Create_sc1_omits_right_stick_and_extra_grips()
    {
        var caps = SteamControllerCapabilities.Create("sc1");
        caps.Inputs.Should().NotContain(i => i.Id == "RightStick");
        caps.Inputs.Should().NotContain(i => i.Id == "RsClick");
        caps.Inputs.Should().NotContain(i => i.Id == "L5");
        caps.Inputs.Should().NotContain(i => i.Id == "R5");
        caps.Inputs.Should().Contain(i => i.Id == "L4" && i.DisplayName == "Left Grip");
        caps.Inputs.Should().Contain(i => i.Id == "R4" && i.DisplayName == "Right Grip");
        caps.Layout.Should().NotContain(h => h.InputId == "RightStick");
        caps.Layout.Should().NotContain(h => h.InputId == "L5");
    }

    [Fact]
    public void GetCapabilities_with_sc1_model_matches_create()
    {
        var registry = new DriverRegistry();
        var caps = registry.GetCapabilities(DriverIds.SteamController, "sc1");
        caps.Inputs.Should().NotContain(i => i.Id == "RightStick");
        caps.Inputs.Should().HaveCount(SteamControllerCapabilities.Create("sc1").Inputs.Count);
    }
}
