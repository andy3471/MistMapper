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

        _foreground.Set("game.exe", @"D:\Games\game.exe");

        _bridge.Status.ActiveProfileId.Should().Be(profile.Id);
        _bridge.Status.ActiveProfileSource.Should().Be(nameof(ActiveProfileSource.GameRule));
        _bridge.Status.CurrentGameExe.Should().Be("game.exe");
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
