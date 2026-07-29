using MistMapper.Shared;

namespace MistMapper.Tests.Profiles;

public sealed class ControllerProfileTests
{
    [Fact]
    public void MigrateLegacyButtonMap_moves_xbox_entries_into_input_map()
    {
        var profile = new ControllerProfile
        {
            ButtonMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["A"] = "B",
                ["X"] = "Y"
            }
        };

        profile.MigrateLegacyButtonMap();

        profile.ButtonMap.Should().BeNull();
        profile.GetAction("A").Xbox.Should().Be(XboxOutput.B);
        profile.GetAction("X").Xbox.Should().Be(XboxOutput.Y);
    }

    [Fact]
    public void GetAction_locked_steam_returns_guide_regardless_of_profile()
    {
        var profile = new ControllerProfile();
        profile.SetAction("Steam", OutputAction.FromXbox(XboxOutput.A));

        profile.GetAction("Steam").Xbox.Should().Be(XboxOutput.Guide);
    }

    [Fact]
    public void EnsureLockedMappings_persists_steam_to_guide()
    {
        var profile = new ControllerProfile();
        profile.InputMap.Remove("Steam");

        profile.EnsureLockedMappings();

        profile.InputMap["Steam"].Xbox.Should().Be(XboxOutput.Guide);
    }

    [Fact]
    public void SetAction_removing_none_clears_mapping()
    {
        var profile = new ControllerProfile();
        profile.SetAction("A", OutputAction.FromXbox(XboxOutput.B));

        profile.SetAction("A", OutputAction.None());

        profile.InputMap.Should().NotContainKey("A");
    }
}
