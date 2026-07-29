using MistMapper.Host.Mapping;
using MistMapper.Shared;

namespace MistMapper.Tests.Mapping;

public sealed class GyroActivationTests
{
    readonly MappingEngine _engine = new();

    static ControllerProfile MouseJoystickProfile()
    {
        var p = OfficialLayouts.CreateMouseJoystick();
        p.Gyro = GyroMode.AsMouseJoystick;
        return p;
    }

    static InputFrame Frame(bool gyro = true, Action<InputFrame>? dig = null)
    {
        var f = new InputFrame();
        // Yaw-dominant sample → virtual stick X via MappingEngine axis swizzle.
        if (gyro) f.Vectors["Gyro"] = (0f, 0.5f);
        dig?.Invoke(f);
        return f;
    }

    [Fact]
    public void Empty_gyro_buttons_always_applies_gyro()
    {
        var profile = MouseJoystickProfile();
        profile.GyroButtons.Clear();

        var state = _engine.Map(Frame(), profile);
        state.ThumbRX.Should().NotBe(0);
    }

    [Fact]
    public void HoldToEnable_requires_selected_button()
    {
        var profile = MouseJoystickProfile();
        profile.GyroButtons = ["RightTrackpad"];
        profile.GyroButtonMode = GyroButtonMode.HoldToEnable;

        _engine.Map(Frame(), profile).ThumbRX.Should().Be(0);

        var on = _engine.Map(Frame(dig: f => f.Digitals["RightTrackpad"] = true), profile);
        on.ThumbRX.Should().NotBe(0);
    }

    [Fact]
    public void HoldToEnable_Any_fires_when_either_button_held()
    {
        var profile = MouseJoystickProfile();
        profile.GyroButtons = ["RightTrackpad", "RightStickTouch"];
        profile.GyroButtonMode = GyroButtonMode.HoldToEnable;
        profile.GyroButtonCombine = GyroButtonCombine.Any;

        var state = _engine.Map(Frame(dig: f => f.Digitals["RightStickTouch"] = true), profile);
        state.ThumbRX.Should().NotBe(0);
    }

    [Fact]
    public void HoldToSuppress_blocks_while_held()
    {
        var profile = MouseJoystickProfile();
        profile.Gyro = GyroMode.AsRightStick; // no mouse-joystick persistence between frames
        profile.GyroButtons = ["LeftTrackpad"];
        profile.GyroButtonMode = GyroButtonMode.HoldToSuppress;

        _engine.Map(Frame(), profile).ThumbRX.Should().NotBe(0);
        _engine.Map(Frame(dig: f => f.Digitals["LeftTrackpad"] = true), profile).ThumbRX.Should().Be(0);
    }
}
