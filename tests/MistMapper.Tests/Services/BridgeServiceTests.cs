using MistMapper.Host.Drivers;
using MistMapper.Host.Services;
using MistMapper.Shared;
using MistMapper.Tests.Fakes;

namespace MistMapper.Tests.Services;

public sealed class BridgeServiceTests : IDisposable
{
    readonly string _tempDir;
    readonly ProfileService _profiles;
    readonly TestSteamState _steam = new();
    readonly TestSessionState _session = new();
    readonly TestForegroundState _foreground = new();
    readonly BridgeService _bridge;

    public BridgeServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "scb-bridge-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _profiles = new ProfileService(_tempDir);
        _bridge = new BridgeService(_profiles, _steam, _session, _foreground);
    }

    public void Dispose()
    {
        _bridge.Dispose();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void SetEnabled_updates_status()
    {
        _bridge.SetEnabled(false);

        _bridge.Status.BridgeEnabled.Should().BeFalse();
    }

    [Fact]
    public void SetActiveProfileManual_updates_status_profile()
    {
        var profile = _profiles.CreateFromLayout(OfficialLayouts.Racing, "Manual pick");

        _bridge.SetActiveProfileManual(profile.Id);

        _bridge.Status.ActiveProfileId.Should().Be(profile.Id);
        _bridge.Status.ActiveProfileSource.Should().Be(nameof(ActiveProfileSource.Manual));
    }

    [Fact]
    public void Foreground_change_applies_game_binding()
    {
        var profile = _profiles.CreateFromLayout(OfficialLayouts.Desktop, "For game");
        _profiles.BindToGame(profile.Id, "game.exe");

        _foreground.Set("game.exe", @"D:\Games\game.exe", "Cool Game");

        _bridge.Status.ActiveProfileId.Should().Be(profile.Id);
        _bridge.Status.ActiveProfileSource.Should().Be(nameof(ActiveProfileSource.GameRule));
        _bridge.Status.CurrentGameExe.Should().Be("game.exe");
        _bridge.Status.CurrentGameName.Should().Be("Cool Game");
    }

    [Fact]
    public void EnsureLayoutForCurrentGame_clones_and_binds_once()
    {
        var original = _profiles.ActiveProfile;
        _foreground.Set("adventure.exe", @"D:\Games\adventure.exe", "Adventure");

        var first = _bridge.EnsureLayoutForCurrentGame(original.Id);
        first.Should().NotBe(original.Id);

        var bound = _profiles.FindBindingForGame("adventure.exe", @"D:\Games\adventure.exe");
        bound.Should().NotBeNull();
        bound!.ProfileId.Should().Be(first);
        bound.DisplayName.Should().Be("Adventure");
        _bridge.Status.ActiveProfileId.Should().Be(first);
        _bridge.Status.ActiveProfileSource.Should().Be(nameof(ActiveProfileSource.GameRule));

        var second = _bridge.EnsureLayoutForCurrentGame(original.Id);
        second.Should().Be(first);
        _profiles.GetUserProfiles().Count(p => p.Name.StartsWith("Adventure", StringComparison.Ordinal))
            .Should().Be(1);
    }

    [Fact]
    public void EnsureLayoutForCurrentGame_noop_without_foreground_game()
    {
        var id = _profiles.ActiveProfile.Id;
        _bridge.EnsureLayoutForCurrentGame(id).Should().Be(id);
    }

    [Fact]
    public void Status_reports_steam_and_session_flags()
    {
        _steam.SetRunning(true);
        _session.SetLocked(true);

        _bridge.Status.SteamRunning.Should().BeTrue();
        _bridge.Status.SessionLocked.Should().BeTrue();
    }

    [Fact]
    public void Drivers_exposes_registry()
    {
        _bridge.Drivers.Primary.Id.Should().Be(DriverIds.SteamController);
    }
}
