using MistMapper.Host.Mapping;
using MistMapper.Host.Viiper;
using MistMapper.Shared;
using MistMapper.Tests.Fakes;

namespace MistMapper.Tests.Mapping;

public sealed class BindingUpgradesTests
{
    [Fact]
    public void MultiBind_regular_holds_two_xbox_buttons()
    {
        var profile = OfficialLayouts.CreateGamepad();
        profile.SetBindingAction("A", ActivatorType.Regular, 0, OutputAction.FromXbox(XboxOutput.A));
        profile.SetBindingAction("A", ActivatorType.Regular, 1, OutputAction.FromXbox(XboxOutput.B));

        var engine = new MappingEngine();
        var frame = new InputFrame();
        frame.Digitals["A"] = true;
        var state = engine.Map(frame, profile);

        (state.Buttons & (uint)Xbox360Buttons.A).Should().NotBe(0);
        (state.Buttons & (uint)Xbox360Buttons.B).Should().NotBe(0);
    }

    [Fact]
    public void LongPress_interrupts_regular_and_holds_long_action()
    {
        var profile = OfficialLayouts.CreateGamepad();
        profile.LongPressMs = 50;
        profile.SetBindingAction("A", ActivatorType.Regular, 0, OutputAction.FromXbox(XboxOutput.A));
        profile.SetBindingAction("A", ActivatorType.LongPress, 0, OutputAction.FromXbox(XboxOutput.X));

        var engine = new MappingEngine();
        var frame = new InputFrame();
        frame.Digitals["A"] = true;

        var early = engine.Map(frame, profile);
        (early.Buttons & (uint)Xbox360Buttons.A).Should().NotBe(0);
        (early.Buttons & (uint)Xbox360Buttons.X).Should().Be(0);

        Thread.Sleep(80);
        var late = engine.Map(frame, profile);
        (late.Buttons & (uint)Xbox360Buttons.A).Should().Be(0, "Regular should release after long press");
        (late.Buttons & (uint)Xbox360Buttons.X).Should().NotBe(0);
    }

    [Fact]
    public void ScrollUp_binding_pulses_wheel_on_press()
    {
        var mouse = new RecordingMouseSink();
        var engine = new MappingEngine(mouse: mouse);
        var profile = OfficialLayouts.CreateGamepad();
        profile.SetAction("R4", OutputAction.FromMouse(MouseButtonOutput.ScrollUp));

        var down = new InputFrame();
        down.Digitals["R4"] = true;
        engine.Map(down, profile);

        engine.TryConsumeMouseWheel(out int wheel).Should().BeTrue();
        wheel.Should().Be(120);
    }
}
