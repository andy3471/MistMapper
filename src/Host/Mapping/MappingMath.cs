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
        TrackballFriction.Off => 0.8f,
        TrackballFriction.Low => 2f,
        TrackballFriction.Medium => 5f,
        TrackballFriction.High => 10f,
        TrackballFriction.ExtraHigh => 16f,
        _ => 5f
    };

    /// <summary>Return-to-center rates for mouse-joystick trackball (lower = longer linger).</summary>
    public static float MouseJoystickTrackballReturnPerSec(TrackballFriction f) => f switch
    {
        TrackballFriction.Off => 0.5f,
        TrackballFriction.Low => 1.0f,
        TrackballFriction.Medium => 2.2f,
        TrackballFriction.High => 5.0f,
        TrackballFriction.ExtraHigh => 9.0f,
        _ => 2.2f
    };
}
