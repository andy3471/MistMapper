namespace MistMapper.Shared;

/// <summary>Steam-style advanced settings for one trackpad surface.</summary>
public sealed class TrackpadSurfaceSettings
{
    /// <summary>When true (As Mouse), finger lift continues motion with friction.</summary>
    public bool TrackballMode { get; set; } = true;

    public TrackballFriction TrackballFriction { get; set; } = TrackballFriction.Medium;

    /// <summary>1.0 = same vertical/horizontal stop rate; higher = stop vertical sooner.</summary>
    public float VerticalFrictionScale { get; set; } = 1f;

    /// <summary>0–100 style filter strength (higher = smoother / more lag).</summary>
    public float Smoothing { get; set; } = 20f;

    /// <summary>Rotate pad axes in degrees (thumb cant).</summary>
    public float RotationDegrees { get; set; }

    /// <summary>Haptic ticks while sliding in As Mouse / As Mouse Joystick.</summary>
    public MouseHapticsIntensity MouseHaptics { get; set; } = MouseHapticsIntensity.Medium;

    /// <summary>1.0 = mild turn; higher = more yaw pixels per radian of flick.</summary>
    public float FlickSensitivity { get; set; } = 1.0f;

    public static TrackpadSurfaceSettings Clone(TrackpadSurfaceSettings? s) => new()
    {
        TrackballMode = s?.TrackballMode ?? true,
        TrackballFriction = s?.TrackballFriction ?? TrackballFriction.Medium,
        VerticalFrictionScale = s?.VerticalFrictionScale ?? 1f,
        Smoothing = s?.Smoothing ?? 20f,
        RotationDegrees = s?.RotationDegrees ?? 0f,
        MouseHaptics = s?.MouseHaptics ?? MouseHapticsIntensity.Medium,
        FlickSensitivity = s?.FlickSensitivity ?? 1.0f
    };
}
