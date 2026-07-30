using MistMapper.Host.Mapping;
using MistMapper.Shared;

namespace MistMapper.Tests.Mapping;

public sealed class MouseHapticsTests
{
    readonly MappingEngine _engine = new();

    static ControllerProfile MousePadProfile(MouseHapticsIntensity haptics)
    {
        var p = ControllerProfile.CreateDefault();
        p.RightTrackpad = TrackpadMode.AsMouse;
        p.RightTrackpadSettings.MouseHaptics = haptics;
        p.RightTrackpadSettings.TrackballMode = false;
        p.RightTrackpadSettings.Smoothing = 0;
        return p;
    }

    static InputFrame PadFrame(float x, float y, bool touch = true)
    {
        var f = new InputFrame();
        f.Digitals["RightTrackpad"] = touch;
        f.Vectors["RightTrackpad"] = (x, y);
        return f;
    }

    [Fact]
    public void AsMouse_movement_emits_haptic_tick()
    {
        var profile = MousePadProfile(MouseHapticsIntensity.High);
        // Seed last position.
        _engine.Map(PadFrame(0f, 0f), profile);
        // Large swipe — High threshold is 0.018.
        _engine.Map(PadFrame(0.2f, 0f), profile);

        _engine.TryConsumeMouseHaptic(out bool right, out byte intensity).Should().BeTrue();
        right.Should().BeTrue();
        intensity.Should().BeGreaterThan(0);
    }

    [Fact]
    public void AsMouse_haptics_off_emits_nothing()
    {
        var profile = MousePadProfile(MouseHapticsIntensity.Off);
        _engine.Map(PadFrame(0f, 0f), profile);
        _engine.Map(PadFrame(0.5f, 0.5f), profile);

        _engine.TryConsumeMouseHaptic(out _, out _).Should().BeFalse();
    }

    [Fact]
    public void AsMouse_tiny_motion_below_threshold_emits_nothing()
    {
        var profile = MousePadProfile(MouseHapticsIntensity.Medium);
        _engine.Map(PadFrame(0f, 0f), profile);
        _engine.Map(PadFrame(0.005f, 0f), profile);

        _engine.TryConsumeMouseHaptic(out _, out _).Should().BeFalse();
    }

    [Fact]
    public void AsMouse_fast_swipe_emits_multiple_ticks()
    {
        var profile = MousePadProfile(MouseHapticsIntensity.High);
        _engine.Map(PadFrame(0f, 0f), profile);
        // 0.2 / 0.014 ≈ 14 crossings, capped at 3 per frame.
        _engine.Map(PadFrame(0.2f, 0f), profile);

        int count = 0;
        while (_engine.TryConsumeMouseHaptic(out _, out _))
            count++;
        count.Should().BeGreaterThan(1);
        count.Should().BeLessThanOrEqualTo(3);
    }

    [Fact]
    public void AsMouse_lift_clears_pending_ticks()
    {
        var profile = MousePadProfile(MouseHapticsIntensity.High);
        _engine.Map(PadFrame(0f, 0f), profile);
        _engine.Map(PadFrame(0.2f, 0f), profile);
        _engine.TryConsumeMouseHaptic(out _, out _).Should().BeTrue();

        // Lift finger — queued ticks for this pad must drop.
        _engine.Map(PadFrame(0.2f, 0f, touch: false), profile);
        _engine.TryConsumeMouseHaptic(out _, out _).Should().BeFalse();
    }

    [Fact]
    public void AsMouse_stationary_jitter_does_not_keep_ticking()
    {
        var profile = MousePadProfile(MouseHapticsIntensity.High);
        _engine.Map(PadFrame(0f, 0f), profile);
        // Seed some travel so accum isn't empty, then hold with pad noise.
        _engine.Map(PadFrame(0.05f, 0f), profile);
        while (_engine.TryConsumeMouseHaptic(out _, out _)) { }

        // Sub-idle jitter for many frames must not drip into new ticks.
        for (int i = 0; i < 200; i++)
        {
            float n = (i % 2 == 0) ? 0.001f : -0.001f;
            _engine.Map(PadFrame(0.05f + n, 0f), profile);
        }

        _engine.TryConsumeMouseHaptic(out _, out _).Should().BeFalse();
    }
}
