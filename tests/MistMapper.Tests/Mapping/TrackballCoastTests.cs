using MistMapper.Host.Mapping;
using MistMapper.Host.Viiper;
using MistMapper.Shared;

namespace MistMapper.Tests.Mapping;

public sealed class TrackballCoastTests
{
    [Fact]
    public void MouseJoystick_trackball_does_not_peg_stick_after_flick()
    {
        var engine = new MappingEngine();
        var profile = OfficialLayouts.CreateMouseJoystick();
        profile.Gyro = GyroMode.Off;
        profile.RightTrackpadSettings.TrackballMode = true;
        profile.RightTrackpadSettings.TrackballFriction = TrackballFriction.Off;
        profile.RightTrackpadSettings.Smoothing = 0;

        MapTouch(engine, profile, 0.0f, 0.0f);
        MapTouch(engine, profile, 0.0f, 0.15f);
        MapTouch(engine, profile, 0.0f, 0.35f);
        MapTouch(engine, profile, 0.0f, 0.55f);

        var afterFlick = engine.Map(UntouchedFrame(), profile);
        afterFlick.ThumbRY.Should().BeGreaterThan(0);

        // Old bug: coast re-applied flick deltas every frame → tip grew/pegged.
        int lastAbs = Math.Abs((int)afterFlick.ThumbRY);
        int peak = lastAbs;
        for (int i = 0; i < 25; i++)
        {
            Thread.Sleep(8);
            var s = engine.Map(UntouchedFrame(), profile);
            int abs = Math.Abs((int)s.ThumbRY);
            abs.Should().BeLessThanOrEqualTo(lastAbs + 800,
                "tip must settle toward center, not grow from runaway coast");
            peak = Math.Max(peak, abs);
            lastAbs = abs;
        }

        lastAbs.Should().BeLessThan(peak, "return friction should reduce tip over time");
        lastAbs.Should().BeLessThan(Math.Max(peak - 1000, peak * 9 / 10));
    }

    [Fact]
    public void MouseJoystick_lower_trackball_friction_lingers_longer()
    {
        short Linger(TrackballFriction friction)
        {
            var engine = new MappingEngine();
            var profile = OfficialLayouts.CreateMouseJoystick();
            profile.Gyro = GyroMode.Off;
            profile.RightTrackpadSettings.TrackballMode = true;
            profile.RightTrackpadSettings.TrackballFriction = friction;
            profile.RightTrackpadSettings.Smoothing = 0;

            MapTouch(engine, profile, 0f, 0f);
            MapTouch(engine, profile, 0f, 0.2f);
            MapTouch(engine, profile, 0f, 0.4f);
            engine.Map(UntouchedFrame(), profile);

            for (int i = 0; i < 12; i++)
            {
                Thread.Sleep(8);
                engine.Map(UntouchedFrame(), profile);
            }

            return engine.Map(UntouchedFrame(), profile).ThumbRY;
        }

        var low = Linger(TrackballFriction.Off);
        var high = Linger(TrackballFriction.ExtraHigh);
        Math.Abs(low).Should().BeGreaterThan(Math.Abs(high),
            "lower trackball friction should leave more tip deflection after the same time");
    }

    [Fact]
    public void MouseJoystick_trackball_friction_applies_while_touching()
    {
        short Hold(TrackballFriction friction)
        {
            var engine = new MappingEngine();
            var profile = OfficialLayouts.CreateMouseJoystick();
            profile.Gyro = GyroMode.Off;
            profile.RightTrackpadSettings.TrackballMode = true;
            profile.RightTrackpadSettings.TrackballFriction = friction;
            profile.RightTrackpadSettings.Smoothing = 0;

            MapTouch(engine, profile, 0f, 0f);
            MapTouch(engine, profile, 0f, 0.25f);
            MapTouch(engine, profile, 0f, 0.5f);

            // Rest on the pad (still touching, no movement) — friction should pull tip in.
            for (int i = 0; i < 18; i++)
            {
                Thread.Sleep(8);
                MapTouch(engine, profile, 0f, 0.5f);
            }

            return engine.Map(new InputFrame
            {
                Digitals = { ["RightTrackpad"] = true },
                Vectors = { ["RightTrackpad"] = (0f, 0.5f) }
            }, profile).ThumbRY;
        }

        var low = Hold(TrackballFriction.Off);
        var high = Hold(TrackballFriction.ExtraHigh);
        Math.Abs(low).Should().BeGreaterThan(Math.Abs(high),
            "lower friction must leave more tip while still touching (not only after lift)");
    }

    [Fact]
    public void MouseJoystick_vertical_friction_scale_decays_y_faster()
    {
        short AfterCoast(float vertScale)
        {
            var engine = new MappingEngine();
            var profile = OfficialLayouts.CreateMouseJoystick();
            profile.Gyro = GyroMode.Off;
            profile.RightTrackpadSettings.TrackballMode = true;
            profile.RightTrackpadSettings.TrackballFriction = TrackballFriction.Low;
            profile.RightTrackpadSettings.VerticalFrictionScale = vertScale;
            profile.RightTrackpadSettings.Smoothing = 0;

            MapTouch(engine, profile, 0f, 0f);
            MapTouch(engine, profile, 0f, 0.4f);
            engine.Map(UntouchedFrame(), profile);
            for (int i = 0; i < 10; i++)
            {
                Thread.Sleep(8);
                engine.Map(UntouchedFrame(), profile);
            }
            return engine.Map(UntouchedFrame(), profile).ThumbRY;
        }

        var normal = AfterCoast(1f);
        var strongVert = AfterCoast(3f);
        Math.Abs(strongVert).Should().BeLessThan(Math.Abs(normal),
            "higher VerticalFrictionScale should settle Y faster");
    }


    static void MapTouch(MappingEngine engine, ControllerProfile profile, float x, float y)
    {
        var frame = new InputFrame();
        frame.Digitals["RightTrackpad"] = true;
        frame.Vectors["RightTrackpad"] = (x, y);
        engine.Map(frame, profile);
    }

    static InputFrame UntouchedFrame()
    {
        var frame = new InputFrame();
        frame.Digitals["RightTrackpad"] = false;
        return frame;
    }
}
