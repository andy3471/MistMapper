using MistMapper.Host.Mapping;
using MistMapper.Host.Viiper;
using MistMapper.Shared;
using MistMapper.Tests.Fakes;

namespace MistMapper.Tests.Mapping;

public sealed class MappingSensitivityTests
{
    readonly RecordingMouseSink _mouse = new();
    readonly MappingEngine _engine;

    public MappingSensitivityTests()
    {
        _engine = new MappingEngine(mouse: _mouse);
    }

    [Fact]
    public void Stick_sensitivity_scales_output()
    {
        var profile = OfficialLayouts.CreateGamepad();
        profile.StickSensitivityX = 2.0f;
        profile.StickSensitivityY = 0.5f;

        var frame = new InputFrame();
        frame.Vectors["LeftStick"] = (0.5f, 0.5f);

        var state = _engine.Map(frame, profile);

        var normalProfile = OfficialLayouts.CreateGamepad();
        var normalState = new MappingEngine().Map(frame, normalProfile);

        Math.Abs(state.ThumbLX).Should().BeGreaterThan(Math.Abs(normalState.ThumbLX));
        Math.Abs(state.ThumbLY).Should().BeLessThan(Math.Abs(normalState.ThumbLY));
    }

    [Fact]
    public void Stick_invert_X_negates_X_axis()
    {
        var profile = OfficialLayouts.CreateGamepad();
        profile.InvertStickX = true;

        var frame = new InputFrame();
        frame.Vectors["LeftStick"] = (0.5f, 0.5f);

        var state = _engine.Map(frame, profile);

        state.ThumbLX.Should().BeNegative();
        state.ThumbLY.Should().BePositive();
    }

    [Fact]
    public void Stick_invert_Y_negates_Y_axis()
    {
        var profile = OfficialLayouts.CreateGamepad();
        profile.InvertStickY = true;

        var frame = new InputFrame();
        frame.Vectors["LeftStick"] = (0.5f, 0.5f);

        var state = _engine.Map(frame, profile);

        state.ThumbLX.Should().BePositive();
        state.ThumbLY.Should().BeNegative();
    }

    [Fact]
    public void Trackpad_deadzone_filters_small_inputs()
    {
        var profile = OfficialLayouts.CreateGamepad();
        profile.LeftTrackpad = TrackpadMode.AsLeftStick;
        profile.TrackpadDeadzone = 0.3f;

        var frame = new InputFrame();
        frame.Digitals["LeftTrackpad"] = true;
        frame.Vectors["LeftTrackpad"] = (0.1f, 0.1f);

        var state = _engine.Map(frame, profile);

        state.ThumbLX.Should().Be(0);
        state.ThumbLY.Should().Be(0);
    }

    [Fact]
    public void Trackpad_sensitivity_scales_output()
    {
        var profile = OfficialLayouts.CreateGamepad();
        profile.LeftTrackpad = TrackpadMode.AsLeftStick;
        profile.TrackpadSensitivityX = 2.0f;
        profile.TrackpadSensitivityY = 2.0f;

        var frame = new InputFrame();
        frame.Digitals["LeftTrackpad"] = true;
        frame.Vectors["LeftTrackpad"] = (0.3f, 0.3f);

        var state = _engine.Map(frame, profile);

        var normalProfile = OfficialLayouts.CreateGamepad();
        normalProfile.LeftTrackpad = TrackpadMode.AsLeftStick;
        var normalState = new MappingEngine().Map(frame, normalProfile);

        Math.Abs(state.ThumbLX).Should().BeGreaterThan(Math.Abs(normalState.ThumbLX));
    }

    [Fact]
    public void Trackpad_invert_negates_axes()
    {
        var profile = OfficialLayouts.CreateGamepad();
        profile.LeftTrackpad = TrackpadMode.AsLeftStick;
        profile.InvertTrackpadX = true;
        profile.InvertTrackpadY = true;

        var frame = new InputFrame();
        frame.Digitals["LeftTrackpad"] = true;
        frame.Vectors["LeftTrackpad"] = (0.5f, 0.5f);

        var state = _engine.Map(frame, profile);

        state.ThumbLX.Should().BeNegative();
        state.ThumbLY.Should().BeNegative();
    }

    [Fact]
    public void Gyro_invert_negates_output()
    {
        var profile = OfficialLayouts.CreateGamepad();
        profile.Gyro = GyroMode.AsRightStick;
        profile.InvertGyroX = true;
        profile.GyroSensitivity = 1f;
        profile.GyroSensitivityX = 1f;
        profile.GyroSensitivityY = 1f;

        var frame = new InputFrame();
        frame.Vectors["Gyro"] = (0.5f, 0.5f);

        var state = _engine.Map(frame, profile);

        var normalProfile = OfficialLayouts.CreateGamepad();
        normalProfile.Gyro = GyroMode.AsRightStick;
        var normalState = new MappingEngine().Map(frame, normalProfile);

        (state.ThumbRX * normalState.ThumbRX).Should().BeNegative("inverted X should flip sign");
    }
}
