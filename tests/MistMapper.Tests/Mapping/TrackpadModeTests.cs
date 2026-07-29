using MistMapper.Host.Mapping;
using MistMapper.Host.Viiper;
using MistMapper.Shared;

namespace MistMapper.Tests.Mapping;

public sealed class TrackpadModeTests
{
    readonly MappingEngine _engine = new();

    [Fact]
    public void FlickStick_maps_strong_touch_to_right_stick_angle()
    {
        var profile = OfficialLayouts.CreateGamepad();
        profile.LeftTrackpad = TrackpadMode.FlickStick;

        var frame = new InputFrame();
        frame.Digitals["LeftTrackpad"] = true;
        frame.Vectors["LeftTrackpad"] = (0.8f, 0.0f);

        var state = _engine.Map(frame, profile);

        state.ThumbRX.Should().NotBe(0, "flick stick should produce right stick output");
    }

    [Fact]
    public void FlickStick_weak_touch_produces_no_output()
    {
        var profile = OfficialLayouts.CreateGamepad();
        profile.LeftTrackpad = TrackpadMode.FlickStick;

        var frame = new InputFrame();
        frame.Digitals["LeftTrackpad"] = true;
        frame.Vectors["LeftTrackpad"] = (0.1f, 0.1f);

        var state = _engine.Map(frame, profile);

        state.ThumbRX.Should().Be(0);
        state.ThumbRY.Should().Be(0);
    }

    [Fact]
    public void ButtonPad_maps_directions_to_face_buttons()
    {
        var profile = OfficialLayouts.CreateGamepad();
        profile.LeftTrackpad = TrackpadMode.ButtonPad;

        var frame = new InputFrame();
        frame.Digitals["LeftTrackpad"] = true;
        frame.Vectors["LeftTrackpad"] = (0.0f, 0.8f);

        var state = _engine.Map(frame, profile);

        (state.Buttons & (uint)Xbox360Buttons.A).Should().NotBe(0, "upward touch should press A");
    }

    [Fact]
    public void ButtonPad_left_maps_to_X()
    {
        var profile = OfficialLayouts.CreateGamepad();
        profile.LeftTrackpad = TrackpadMode.ButtonPad;

        var frame = new InputFrame();
        frame.Digitals["LeftTrackpad"] = true;
        frame.Vectors["LeftTrackpad"] = (-0.8f, 0.0f);

        var state = _engine.Map(frame, profile);

        (state.Buttons & (uint)Xbox360Buttons.X).Should().NotBe(0, "left touch should press X");
    }

    [Fact]
    public void ButtonPad_right_maps_to_B()
    {
        var profile = OfficialLayouts.CreateGamepad();
        profile.LeftTrackpad = TrackpadMode.ButtonPad;

        var frame = new InputFrame();
        frame.Digitals["LeftTrackpad"] = true;
        frame.Vectors["LeftTrackpad"] = (0.8f, 0.0f);

        var state = _engine.Map(frame, profile);

        (state.Buttons & (uint)Xbox360Buttons.B).Should().NotBe(0, "right touch should press B");
    }

    [Fact]
    public void ButtonPad_down_maps_to_Y()
    {
        var profile = OfficialLayouts.CreateGamepad();
        profile.LeftTrackpad = TrackpadMode.ButtonPad;

        var frame = new InputFrame();
        frame.Digitals["LeftTrackpad"] = true;
        frame.Vectors["LeftTrackpad"] = (0.0f, -0.8f);

        var state = _engine.Map(frame, profile);

        (state.Buttons & (uint)Xbox360Buttons.Y).Should().NotBe(0, "downward touch should press Y");
    }

    [Fact]
    public void ScrollWheel_produces_mouse_delta_on_vertical_swipe()
    {
        var profile = OfficialLayouts.CreateGamepad();
        profile.RightTrackpad = TrackpadMode.ScrollWheel;

        var frame1 = new InputFrame();
        frame1.Digitals["RightTrackpad"] = true;
        frame1.Vectors["RightTrackpad"] = (0f, 0f);
        _engine.Map(frame1, profile);

        var frame2 = new InputFrame();
        frame2.Digitals["RightTrackpad"] = true;
        frame2.Vectors["RightTrackpad"] = (0f, 0.5f);
        _engine.Map(frame2, profile);

        _engine.TryConsumeMouseDelta(out _, out int dy).Should().BeTrue();
        dy.Should().NotBe(0, "vertical swipe in scroll mode should produce vertical mouse delta");
    }

    [Fact]
    public void Off_mode_produces_no_output()
    {
        var profile = OfficialLayouts.CreateGamepad();
        profile.LeftTrackpad = TrackpadMode.Off;

        var frame = new InputFrame();
        frame.Digitals["LeftTrackpad"] = true;
        frame.Vectors["LeftTrackpad"] = (0.8f, 0.8f);

        var state = _engine.Map(frame, profile);

        state.ThumbLX.Should().Be(0);
        state.ThumbLY.Should().Be(0);
    }

    [Fact]
    public void MouseJoystick_relative_swipe_deflects_right_stick()
    {
        var profile = OfficialLayouts.CreateGamepad();
        profile.RightTrackpad = TrackpadMode.AsMouseJoystick;
        profile.Gyro = GyroMode.Off;

        var seed = new InputFrame();
        seed.Digitals["RightTrackpad"] = true;
        seed.Vectors["RightTrackpad"] = (0f, 0f);
        _engine.Map(seed, profile);

        var swipe = new InputFrame();
        swipe.Digitals["RightTrackpad"] = true;
        swipe.Vectors["RightTrackpad"] = (0.25f, 0.1f);
        var state = _engine.Map(swipe, profile);

        state.ThumbRX.Should().BeGreaterThan(0, "horizontal swipe should aim right stick");
        state.ThumbRY.Should().BeGreaterThan(0, "vertical swipe should aim right stick");
        _engine.TryConsumeMouseDelta(out _, out _).Should().BeFalse("mouse joystick must not move the OS cursor");
    }

    [Fact]
    public void MouseJoystick_gyro_deflects_right_stick()
    {
        var profile = OfficialLayouts.CreateGamepad();
        profile.RightTrackpad = TrackpadMode.Off;
        profile.Gyro = GyroMode.AsMouseJoystick;

        var frame = new InputFrame();
        frame.Vectors["Gyro"] = (0.2f, 0.4f);
        var state = _engine.Map(frame, profile);

        state.ThumbRX.Should().NotBe(0);
        state.ThumbRY.Should().NotBe(0);
        _engine.TryConsumeMouseDelta(out _, out _).Should().BeFalse();
    }
}
