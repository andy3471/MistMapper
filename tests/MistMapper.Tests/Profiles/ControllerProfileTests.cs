using MistMapper.Shared;

namespace MistMapper.Tests.Profiles;

public sealed class ControllerProfileTests
{
    [Fact]
    public void MigrateLegacyButtonMap_moves_xbox_entries_into_bindings()
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
        profile.InputMap.Should().BeNull();
        profile.GetAction("A").Xbox.Should().Be(XboxOutput.B);
        profile.GetAction("X").Xbox.Should().Be(XboxOutput.Y);
    }

    [Fact]
    public void MigrateInputMap_to_bindings()
    {
        var profile = new ControllerProfile
        {
            InputMap = new Dictionary<string, OutputAction>(StringComparer.OrdinalIgnoreCase)
            {
                ["A"] = OutputAction.FromXbox(XboxOutput.B)
            }
        };

        profile.MigrateLegacyButtonMap();

        profile.InputMap.Should().BeNull();
        profile.Bindings.Should().ContainKey("A");
        profile.GetAction("A").Xbox.Should().Be(XboxOutput.B);
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
        profile.Bindings.Remove("Steam");

        profile.EnsureLockedMappings();

        profile.Bindings["Steam"][0].Actions[0].Xbox.Should().Be(XboxOutput.Guide);
    }

    [Fact]
    public void SetAction_removing_none_clears_mapping()
    {
        var profile = new ControllerProfile();
        profile.SetAction("A", OutputAction.FromXbox(XboxOutput.B));

        profile.SetAction("A", OutputAction.None());

        profile.Bindings.Should().NotContainKey("A");
    }

    [Fact]
    public void SetBindingAction_supports_second_regular_slot()
    {
        var profile = new ControllerProfile();
        profile.SetBindingAction("A", ActivatorType.Regular, 0, OutputAction.FromXbox(XboxOutput.A));
        profile.SetBindingAction("A", ActivatorType.Regular, 1, OutputAction.FromKey(0x20));

        var reg = profile.GetBindings("A").First(b => b.Activator == ActivatorType.Regular);
        reg.Actions.Should().HaveCount(2);
        reg.Actions[0].Xbox.Should().Be(XboxOutput.A);
        reg.Actions[1].VirtualKey.Should().Be(0x20);
    }
}
