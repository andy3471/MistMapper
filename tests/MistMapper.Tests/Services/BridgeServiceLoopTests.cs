using MistMapper.Host.Drivers;
using MistMapper.Host.Services;
using MistMapper.Host.Viiper;
using MistMapper.Shared;
using MistMapper.Tests.Fakes;

namespace MistMapper.Tests.Services;

public sealed class BridgeServiceLoopTests : IDisposable
{
    readonly string _tempDir;
    readonly ProfileService _profiles;
    readonly TestSteamState _steam = new();
    readonly TestSessionState _session = new();
    readonly TestForegroundState _foreground = new();
    readonly FakeViiperHealth _viiperHealth = new();
    readonly FakeViiperClient _viiperClient = new();
    readonly FakeControllerDriver _driver = new();
    readonly BridgeService _bridge;

    public BridgeServiceLoopTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "scb-loop-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _profiles = new ProfileService(_tempDir);
        _bridge = new BridgeService(
            _profiles,
            _steam,
            _session,
            _foreground,
            drivers: new DriverRegistry([_driver]),
            viiperHealth: _viiperHealth,
            viiperFactory: () => _viiperClient);
    }

    public void Dispose()
    {
        _bridge.Dispose();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task Start_maps_controller_input_to_viiper()
    {
        var frame = new InputFrame();
        frame.Digitals["A"] = true;
        _driver.Enqueue(frame);

        _bridge.Start();

        var ok = await WaitUntilAsync(
            () => _viiperClient.Inputs.Any(i => (i.Buttons & (uint)Xbox360Buttons.A) != 0),
            TimeSpan.FromSeconds(5));

        ok.Should().BeTrue("bridge loop should forward mapped A press to VIIPER");
        _bridge.Status.ViiperConnected.Should().BeTrue();
        _bridge.Status.ControllerConnected.Should().BeTrue();
    }

    [Fact]
    public async Task Start_pauses_when_steam_running()
    {
        _steam.SetRunning(true);
        _driver.Enqueue(new InputFrame { Digitals = { ["A"] = true } });

        _bridge.Start();
        await Task.Delay(600);

        _viiperClient.Inputs.Should().BeEmpty();
        _bridge.Status.SteamRunning.Should().BeTrue();
        _bridge.Status.State.Should().Be(BridgeRunState.PausedSteam);
    }

    [Fact]
    public void GameBar_open_sets_override_active_in_status()
    {
        var gameBar = new TestGameBarState();
        using var bridge = new BridgeService(
            _profiles,
            _steam,
            _session,
            _foreground,
            gameBar,
            drivers: new DriverRegistry([_driver]),
            viiperHealth: _viiperHealth,
            viiperFactory: () => _viiperClient);

        bridge.Status.GameBarOverrideActive.Should().BeFalse();
        gameBar.SetOpen(true);
        bridge.Status.GameBarOverrideActive.Should().BeTrue();
        gameBar.SetOpen(false);
        bridge.Status.GameBarOverrideActive.Should().BeFalse();
    }

    [Fact]
    public async Task Viiper_rumble_is_forwarded_to_physical_driver()
    {
        _driver.Enqueue(new InputFrame());
        _bridge.Start();

        var connected = await WaitUntilAsync(
            () => _viiperClient.IsConnected && _bridge.Status.ControllerConnected,
            TimeSpan.FromSeconds(5));
        connected.Should().BeTrue();

        _viiperClient.RaiseRumble(200, 40);

        var ok = await WaitUntilAsync(
            () => _driver.RumbleHistory.Any(r => r.Left == 200 && r.Right == 40),
            TimeSpan.FromSeconds(2));
        ok.Should().BeTrue("VIIPER rumble should reach the physical controller driver");
    }

    static async Task<bool> WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (predicate()) return true;
            await Task.Delay(25);
        }
        return predicate();
    }
}
