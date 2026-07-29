using MistMapper.Host.Drivers;
using MistMapper.Host.Mapping;
using MistMapper.Host.Services;
using MistMapper.Shared;
using MistMapper.Tests.Fakes;

namespace MistMapper.Tests.Services;

public sealed class MultiControllerTests : IDisposable
{
    readonly string _tempDir;
    readonly ProfileService _profiles;

    public MultiControllerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "mist-multi-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _profiles = new ProfileService(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void EnsureControllerSlot_appends_new_pads_and_restores_order()
    {
        var a = _profiles.EnsureControllerSlot("pad-a", "SC2", "sc2");
        var b = _profiles.EnsureControllerSlot("pad-b", "SC1", "sc1");

        a.Order.Should().Be(0);
        b.Order.Should().Be(1);

        _profiles.SetControllerSlotOrder(
        [
            new ControllerSlot { DeviceKey = "pad-b", Order = 0 },
            new ControllerSlot { DeviceKey = "pad-a", Order = 1 }
        ]);

        var slots = _profiles.GetControllerSlots();
        slots.Select(s => s.DeviceKey).Should().ContainInOrder("pad-b", "pad-a");
        slots[0].Order.Should().Be(0);
        slots[1].Order.Should().Be(1);

        var again = _profiles.EnsureControllerSlot("pad-a", "SC2", "sc2");
        again.Order.Should().Be(1);
    }

    [Fact]
    public void SetControllerSlotProfile_override_is_keyed_by_device_key()
    {
        _profiles.EnsureControllerSlot("pad-a", "A", "sc2");
        var copy = _profiles.SaveAsProfile(_profiles.ActiveProfile.Id, "Pad A layout");
        _profiles.SetControllerSlotProfile("pad-a", copy.Id);

        var slot = _profiles.FindControllerSlot("pad-a");
        slot.Should().NotBeNull();
        slot!.ProfileId.Should().Be(copy.Id);
    }

    [Fact]
    public void MappingEngine_skips_keyboard_when_allowKeyboardMouse_false()
    {
        var kb = new RecordingKeyboardSink();
        var mouse = new RecordingMouseSink();
        var engine = new MappingEngine(kb, mouse);
        var profile = ControllerProfile.CreateDefault();
        profile.SetAction("A", OutputAction.FromKey(0x41));

        var frame = new InputFrame();
        frame.Digitals["A"] = true;

        engine.Map(frame, profile, allowKeyboardMouse: false);
        kb.Keys.Should().BeEmpty();

        engine.Map(frame, profile, allowKeyboardMouse: true);
        kb.Keys.Should().Contain(k => k.Vk == 0x41 && k.Down);
    }

    [Fact]
    public async Task Bridge_opens_multiple_injected_pads_and_forwards_to_separate_viiper()
    {
        var steam = new TestSteamState();
        var session = new TestSessionState();
        var foreground = new TestForegroundState();
        var health = new FakeViiperHealth();
        var clients = new List<FakeViiperClient>();
        var d0 = new FakeControllerDriver { DeviceKey = "pad-0", DisplayName = "Pad 0", ControllerModel = "sc2" };
        var d1 = new FakeControllerDriver { DeviceKey = "pad-1", DisplayName = "Pad 1", ControllerModel = "sc1" };
        d0.Enqueue(new InputFrame { Digitals = { ["A"] = true } });
        d1.Enqueue(new InputFrame { Digitals = { ["B"] = true } });

        using var bridge = new BridgeService(
            _profiles,
            steam,
            session,
            foreground,
            drivers: new DriverRegistry([d0, d1]),
            viiperHealth: health,
            viiperFactory: () =>
            {
                var c = new FakeViiperClient();
                clients.Add(c);
                return c;
            });

        bridge.Start();

        var ok = await WaitUntilAsync(
            () => clients.Count >= 2 && clients.Any(c => c.Inputs.Count > 0),
            TimeSpan.FromSeconds(5));
        ok.Should().BeTrue();
        bridge.Status.Controllers.Should().HaveCount(2);
        bridge.Status.ControllerConnected.Should().BeTrue();
    }

    [Fact]
    public async Task Bridge_keyboard_mouse_only_on_lowest_order_pad()
    {
        var steam = new TestSteamState();
        var session = new TestSessionState();
        var foreground = new TestForegroundState();
        var health = new FakeViiperHealth();
        var kb = new RecordingKeyboardSink();
        var mouse = new RecordingMouseSink();

        _profiles.EnsureControllerSlot("pad-primary", "P1", "sc2");
        _profiles.EnsureControllerSlot("pad-secondary", "P2", "sc1");
        _profiles.SetControllerSlotOrder(
        [
            new ControllerSlot { DeviceKey = "pad-primary", Order = 0 },
            new ControllerSlot { DeviceKey = "pad-secondary", Order = 1 }
        ]);

        var primary = new FakeControllerDriver { DeviceKey = "pad-primary", DisplayName = "P1" };
        var secondary = new FakeControllerDriver { DeviceKey = "pad-secondary", DisplayName = "P2" };

        var profile = _profiles.ActiveProfile;
        profile.SetAction("A", OutputAction.FromKey(0x41));
        _profiles.Upsert(profile);

        // Only secondary presses A — should not inject keys (not lowest order).
        secondary.Enqueue(new InputFrame { Digitals = { ["A"] = true } });
        primary.Enqueue(new InputFrame());

        using var bridge = new BridgeService(
            _profiles,
            steam,
            session,
            foreground,
            drivers: new DriverRegistry([primary, secondary]),
            mouse: mouse,
            keyboard: kb,
            viiperHealth: health,
            viiperFactory: () => new FakeViiperClient());

        bridge.Start();
        await Task.Delay(800);
        kb.Keys.Should().BeEmpty("secondary pad must not inject keyboard");

        // Primary presses A — should inject.
        primary.Enqueue(new InputFrame { Digitals = { ["A"] = true } });
        var ok = await WaitUntilAsync(() => kb.Keys.Any(k => k.Vk == 0x41 && k.Down), TimeSpan.FromSeconds(3));
        ok.Should().BeTrue("primary pad should inject keyboard");
    }

    [Fact]
    public void Resolve_profile_prefers_slot_override()
    {
        var shared = _profiles.ActiveProfile;
        var unique = _profiles.SaveAsProfile(shared.Id, "Unique");
        _profiles.EnsureControllerSlot("pad-x", "X", "sc2");
        _profiles.SetControllerSlotProfile("pad-x", unique.Id);

        var steam = new TestSteamState();
        var session = new TestSessionState();
        var foreground = new TestForegroundState();
        var d0 = new FakeControllerDriver { DeviceKey = "pad-x" };

        using var bridge = new BridgeService(
            _profiles,
            steam,
            session,
            foreground,
            drivers: new DriverRegistry([d0]),
            viiperHealth: new FakeViiperHealth(),
            viiperFactory: () => new FakeViiperClient());

        bridge.SetSelectedController("pad-x");
        // Force slot metadata into bridge via start sync briefly
        bridge.Start();
        Thread.Sleep(400);
        var resolved = bridge.GetSelectedResolvedProfile();
        resolved.Id.Should().Be(unique.Id);
        bridge.SelectedPadHasProfileOverride().Should().BeTrue();
    }

    [Fact]
    public async Task DualSense_and_SC_fakes_coexist_and_rumble_forwards()
    {
        var steam = new TestSteamState();
        var session = new TestSessionState();
        var foreground = new TestForegroundState();
        var health = new FakeViiperHealth();
        var sc = new FakeControllerDriver
        {
            Id = DriverIds.SteamController,
            DeviceKey = "sc-pad",
            DisplayName = "SC",
            ControllerModel = "sc2"
        };
        var ds = new FakeControllerDriver
        {
            Id = DriverIds.DualSense,
            DeviceKey = "ds-pad",
            DisplayName = "DualSense",
            ControllerModel = "dualsense",
            Capabilities = DualSenseCapabilities.Create()
        };
        sc.Enqueue(new InputFrame());
        ds.Enqueue(new InputFrame());

        using var bridge = new BridgeService(
            _profiles,
            steam,
            session,
            foreground,
            drivers: new DriverRegistry([sc, ds]),
            viiperHealth: health,
            viiperFactory: () => new FakeViiperClient());

        bridge.Start();

        var ready = await WaitUntilAsync(
            () => bridge.Status.Controllers.Count >= 2,
            TimeSpan.FromSeconds(5));
        ready.Should().BeTrue();

        bridge.SetSelectedController("ds-pad");
        await Task.Delay(200);
        bridge.Status.ActiveDriverId.Should().Be(DriverIds.DualSense);
        bridge.Status.ControllerModel.Should().Be("dualsense");

        await bridge.IdentifyControllerAsync("ds-pad");
        ds.RumbleHistory.Should().NotBeEmpty();
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
