using MistMapper.Host.Mapping;
using MistMapper.Shared;
using MistMapper.Tests.Fakes;

namespace MistMapper.Tests.Mapping;

public sealed class MappingEngineSinkTests
{
    [Fact]
    public void Map_keyboard_action_presses_virtual_key()
    {
        var keyboard = new RecordingKeyboardSink();
        var mouse = new RecordingMouseSink();
        var engine = new MappingEngine(keyboard, mouse);
        var profile = OfficialLayouts.CreateKeyboardMouse();
        var frame = new InputFrame();
        frame.Digitals["A"] = true;

        engine.Map(frame, profile);

        keyboard.Keys.Should().Contain(k => k.Vk == 0x20 && k.Down);
        mouse.Buttons.Should().BeEmpty();
    }

    [Fact]
    public void Map_mouse_button_action_presses_mouse_button()
    {
        var keyboard = new RecordingKeyboardSink();
        var mouse = new RecordingMouseSink();
        var engine = new MappingEngine(keyboard, mouse);
        var profile = OfficialLayouts.CreateDesktop();
        var frame = new InputFrame();
        frame.Digitals["RightTrackpadClick"] = true;

        engine.Map(frame, profile);

        mouse.Buttons.Should().Contain(b => b.Button == MouseButtonOutput.Left && b.Down);
    }

    [Fact]
    public void Map_releasing_digital_releases_injected_key()
    {
        var keyboard = new RecordingKeyboardSink();
        var mouse = new RecordingMouseSink();
        var engine = new MappingEngine(keyboard, mouse);
        var profile = OfficialLayouts.CreateKeyboardMouse();
        var pressed = new InputFrame();
        pressed.Digitals["A"] = true;
        engine.Map(pressed, profile);

        var released = new InputFrame();
        engine.Map(released, profile);

        keyboard.Keys.Should().Contain(k => k.Vk == 0x20 && !k.Down);
    }

    [Fact]
    public void TryConsumeMouseDelta_emits_accumulated_trackpad_motion()
    {
        var keyboard = new RecordingKeyboardSink();
        var mouse = new RecordingMouseSink();
        var engine = new MappingEngine(keyboard, mouse);
        var profile = OfficialLayouts.CreateDesktop();
        profile.RightTrackpad = TrackpadMode.AsMouse;

        var firstTouch = new InputFrame();
        firstTouch.Digitals["RightTrackpad"] = true;
        firstTouch.Vectors["RightTrackpad"] = (0f, 0f);
        engine.Map(firstTouch, profile);

        var swipe = new InputFrame();
        swipe.Digitals["RightTrackpad"] = true;
        swipe.Vectors["RightTrackpad"] = (0.5f, 0f);
        engine.Map(swipe, profile);

        engine.TryConsumeMouseDelta(out var dx, out var dy).Should().BeTrue();
        dx.Should().NotBe(0);
        dy.Should().Be(0);
    }

    [Fact]
    public void ReleaseAllInjected_clears_held_keys_and_buttons()
    {
        var keyboard = new RecordingKeyboardSink();
        var mouse = new RecordingMouseSink();
        var engine = new MappingEngine(keyboard, mouse);
        var profile = OfficialLayouts.CreateDesktop();
        var frame = new InputFrame();
        frame.Digitals["RightTrackpadClick"] = true;
        engine.Map(frame, profile);

        engine.ReleaseAllInjected();

        mouse.Buttons.Should().Contain(b => b.Button == MouseButtonOutput.Left && !b.Down);
    }
}
