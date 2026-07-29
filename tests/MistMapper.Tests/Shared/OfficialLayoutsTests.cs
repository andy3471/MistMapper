using MistMapper.Shared;

namespace MistMapper.Tests.Shared;

public sealed class OfficialLayoutsTests
{
    public static IEnumerable<object[]> LayoutIds() =>
        OfficialLayouts.All.Select(info => new object[] { info.Id });

    [Theory]
    [MemberData(nameof(LayoutIds))]
    public void Create_sets_layout_id(string layoutId)
    {
        var profile = OfficialLayouts.Create(layoutId);

        profile.LayoutId.Should().Be(layoutId);
        profile.InputMap.Should().ContainKey("Steam");
        profile.GetAction("Steam").Xbox.Should().Be(XboxOutput.Guide);
    }

    [Fact]
    public void CreateDesktop_uses_mouse_trackpads()
    {
        var profile = OfficialLayouts.CreateDesktop();

        profile.LeftTrackpad.Should().Be(TrackpadMode.AsMouse);
        profile.RightTrackpad.Should().Be(TrackpadMode.AsMouse);
        profile.GetAction("RightTrackpadClick").Kind.Should().Be(OutputActionKind.MouseButton);
    }

    [Fact]
    public void CreateMouseJoystick_uses_mouse_joystick_modes()
    {
        var profile = OfficialLayouts.CreateMouseJoystick();

        profile.RightTrackpad.Should().Be(TrackpadMode.AsMouseJoystick);
        profile.Gyro.Should().Be(GyroMode.AsMouseJoystick);
        profile.GetAction("RightTrackpadClick").Xbox.Should().Be(XboxOutput.RsClick);
    }

    [Fact]
    public void CreateRacing_maps_paddles_to_shoulder_buttons()
    {
        var profile = OfficialLayouts.CreateRacing();

        profile.GetAction("L4").Xbox.Should().Be(XboxOutput.Lb);
        profile.GetAction("R4").Xbox.Should().Be(XboxOutput.Rb);
    }

    [Fact]
    public void Create_unknown_layout_falls_back_to_gamepad()
    {
        var profile = OfficialLayouts.Create("not-a-real-layout");

        profile.LayoutId.Should().Be(OfficialLayouts.Gamepad);
    }
}
