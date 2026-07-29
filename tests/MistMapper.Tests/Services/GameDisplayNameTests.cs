using MistMapper.Host.Services;

namespace MistMapper.Tests.Services;

public sealed class GameDisplayNameTests
{
    [Fact]
    public void Resolve_falls_back_to_exe_name_without_extension()
    {
        GameDisplayName.Resolve(null, "eldenring.exe").Should().Be("eldenring");
    }

    [Fact]
    public void Resolve_prefers_window_title_over_exe()
    {
        GameDisplayName.Resolve(null, "game.exe", "Cool Game")
            .Should().Be("Cool Game");
    }

    [Fact]
    public void Resolve_strips_unreal_engine_suffix_from_title()
    {
        GameDisplayName.Resolve(null, "game.exe", "My Title - Unreal Engine")
            .Should().Be("My Title");
    }

    [Fact]
    public void Resolve_returns_Game_when_nothing_available()
    {
        GameDisplayName.Resolve(null, null).Should().Be("Game");
    }
}
