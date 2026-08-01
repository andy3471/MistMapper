using MistMapper.Shared;

namespace MistMapper.Host.Mapping;

static class MappingMath
{
    public static (float X, float Y) Rotate2D(float x, float y, float degrees)
    {
        if (Math.Abs(degrees) < 0.01f) return (x, y);
        float rad = degrees * (MathF.PI / 180f);
        float c = MathF.Cos(rad);
        float s = MathF.Sin(rad);
        return (x * c - y * s, x * s + y * c);
    }

    public static short ClampToShort(float v) => (short)Math.Clamp((int)v, short.MinValue, short.MaxValue);

    public static float TrackballFrictionPerSec(TrackballFriction f) => f switch
    {
        // "Off" = lightest coast, not zero — ice-smooth 0.8 made mouse feel endlessly slidy.
        TrackballFriction.Off => 2.5f,
        TrackballFriction.Low => 4f,
        TrackballFriction.Medium => 7f,
        TrackballFriction.High => 12f,
        TrackballFriction.ExtraHigh => 20f,
        _ => 7f
    };

    /// <summary>Return-to-center rates for mouse-joystick trackball (lower = longer linger).</summary>
    public static float MouseJoystickTrackballReturnPerSec(TrackballFriction f) => f switch
    {
        TrackballFriction.Off => 1.2f,
        TrackballFriction.Low => 2.0f,
        TrackballFriction.Medium => 3.5f,
        TrackballFriction.High => 6.0f,
        TrackballFriction.ExtraHigh => 11.0f,
        _ => 3.5f
    };
}
