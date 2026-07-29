using MistMapper.Host.Mapping;
using MistMapper.Host.Viiper;
using MistMapper.Shared;

namespace MistMapper.Tests.Mapping;

public sealed class MappingEngineTests
{
    readonly MappingEngine _engine = new();

    [Fact]
    public void Map_pressing_A_sets_xbox_A_button()
    {
        var profile = OfficialLayouts.CreateGamepad();
        var frame = new InputFrame();
        frame.Digitals["A"] = true;

        var state = _engine.Map(frame, profile);

        state.Buttons.Should().Be((uint)Xbox360Buttons.A);
    }

    [Fact]
    public void Map_steam_always_sets_guide_even_when_profile_differs()
    {
        var profile = OfficialLayouts.CreateGamepad();
        profile.SetAction("Steam", OutputAction.FromXbox(XboxOutput.A));
        var frame = new InputFrame();
        frame.Digitals["Steam"] = true;

        var state = _engine.Map(frame, profile);

        state.Buttons.Should().Be((uint)Xbox360Buttons.Guide);
    }

    [Fact]
    public void Map_trigger_applies_deadzone()
    {
        var profile = OfficialLayouts.CreateGamepad();
        profile.TriggerDeadzone = 0.5f;
        var frame = new InputFrame();
        frame.Analogs["Rt"] = 0.25f;

        var state = _engine.Map(frame, profile);

        state.RightTrigger.Should().Be(0);
    }

    [Fact]
    public void Map_left_stick_passes_through_normalized_values()
    {
        var profile = OfficialLayouts.CreateGamepad();
        var frame = new InputFrame();
        frame.Vectors["LeftStick"] = (0.5f, -1f);

        var state = _engine.Map(frame, profile);

        state.ThumbLX.Should().BeGreaterThan(0);
        state.ThumbLY.Should().BeLessThan(0);
    }

    [Theory]
    [InlineData(XboxOutput.A, Xbox360Buttons.A)]
    [InlineData(XboxOutput.B, Xbox360Buttons.B)]
    [InlineData(XboxOutput.X, Xbox360Buttons.X)]
    [InlineData(XboxOutput.Y, Xbox360Buttons.Y)]
    [InlineData(XboxOutput.Lb, Xbox360Buttons.LeftShoulder)]
    [InlineData(XboxOutput.Rb, Xbox360Buttons.RightShoulder)]
    [InlineData(XboxOutput.Back, Xbox360Buttons.Back)]
    [InlineData(XboxOutput.Start, Xbox360Buttons.Start)]
    [InlineData(XboxOutput.Guide, Xbox360Buttons.Guide)]
    [InlineData(XboxOutput.DpadUp, Xbox360Buttons.DpadUp)]
    public void ToButtonFlag_maps_xbox_outputs(XboxOutput output, Xbox360Buttons expected)
    {
        MappingEngine.ToButtonFlag(output).Should().Be((uint)expected);
    }
}
