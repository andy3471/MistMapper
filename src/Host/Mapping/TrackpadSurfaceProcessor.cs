using System.Diagnostics;
using MistMapper.Host.Viiper;
using MistMapper.Shared;

namespace MistMapper.Host.Mapping;

sealed class TrackpadSurfaceProcessor
{
    /// <summary>Pixels of cursor travel for a full-pad finger swipe (normalized Δ≈2).</summary>
    const float TrackpadMouseSensitivity = 900f;

    /// <summary>Pad Δ≈0.2 at gain 5 → full stick tip (Steam-like mouse→stick).</summary>
    const float MouseJoystickPadGain = 5f;

    readonly Dictionary<string, (float X, float Y)> _padMouseLast = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, (float Vx, float Vy)> _padTrackballVel = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, (float X, float Y)> _padSmooth = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, float> _mouseHapticAccum = new(StringComparer.OrdinalIgnoreCase);
    readonly Queue<(bool RightPad, byte Intensity)> _mouseHapticTicks = new();
    readonly Dictionary<string, FlickPadState> _flickPads = new(StringComparer.OrdinalIgnoreCase);
    double _wheelAccum;

    sealed class FlickPadState
    {
        public float LastAngle;
        public float AccumYaw;
        public bool HasAngle;
    }

    public double WheelAccum => _wheelAccum;

    public void AddWheelDelta(double delta) => _wheelAccum += delta;

    public bool TryConsumeMouseHaptic(out bool rightPad, out byte intensity)
    {
        if (_mouseHapticTicks.Count == 0)
        {
            rightPad = false;
            intensity = 0;
            return false;
        }

        (rightPad, intensity) = _mouseHapticTicks.Dequeue();
        return true;
    }

    /// <summary>
    /// Mouse-joystick "trackball" = slower return-to-center after a flick, not a
    /// per-frame impulse loop (that pegs the stick and spins the camera).
    /// </summary>
    public static float GetMouseJoystickReturnFriction(ControllerProfile profile, InputFrame frame, float defaultFrictionPerSec)
    {
        static float FromPad(
            TrackpadMode mode,
            TrackpadSurfaceSettings? settings,
            bool touching,
            float defaultFriction,
            Func<TrackballFriction, float> trackballReturnPerSec)
        {
            if (mode != TrackpadMode.AsMouseJoystick || settings is not { TrackballMode: true })
                return -1f;
            // While still touching, keep a snappier return so resting on the pad centers.
            if (touching)
                return Math.Max(defaultFriction, trackballReturnPerSec(settings.TrackballFriction));
            return trackballReturnPerSec(settings.TrackballFriction);
        }

        float left = FromPad(
            profile.LeftTrackpad,
            profile.LeftTrackpadSettings,
            frame.GetDigital("LeftTrackpad"),
            defaultFrictionPerSec,
            MappingMath.MouseJoystickTrackballReturnPerSec);
        float right = FromPad(
            profile.RightTrackpad,
            profile.RightTrackpadSettings,
            frame.GetDigital("RightTrackpad"),
            defaultFrictionPerSec,
            MappingMath.MouseJoystickTrackballReturnPerSec);
        if (left < 0 && right < 0)
            return defaultFrictionPerSec;
        if (left < 0) return right;
        if (right < 0) return left;
        return Math.Min(left, right);
    }

    public void ApplyTrackpad(
        TrackpadMode mode,
        bool touching,
        InputFrame frame,
        string id,
        TrackpadSurfaceSettings? surface,
        ControllerProfile profile,
        ref Xbox360InputState state,
        ref uint buttons,
        Action<float, float>? addMouseJoystick,
        Action<double, double> addMouseDelta)
    {
        surface ??= new TrackpadSurfaceSettings();
        if (mode == TrackpadMode.Off) return;

        if (!touching)
        {
            if (mode == TrackpadMode.FlickStick)
                FinishFlick(id, surface, addMouseDelta);
            _padMouseLast.Remove(id);
            _padSmooth.Remove(id);
            ClearMouseHapticState(id);
            // Trackball impulse coast is OS-mouse only.
            if (!surface.TrackballMode || mode != TrackpadMode.AsMouse)
                _padTrackballVel.Remove(id);
            return;
        }

        if (!frame.TryGetVector(id, out var nx, out var ny)) return;

        float sensX = profile.TrackpadSensitivityX;
        float sensY = profile.TrackpadSensitivityY;
        bool invX = profile.InvertTrackpadX;
        bool invY = profile.InvertTrackpadY;
        float dz = profile.TrackpadDeadzone;

        float ax = Math.Abs(nx) < dz ? 0 : nx;
        float ay = Math.Abs(ny) < dz ? 0 : ny;
        (ax, ay) = MappingMath.Rotate2D(ax, ay, surface.RotationDegrees);
        ax *= sensX * (invX ? -1 : 1);
        ay *= sensY * (invY ? -1 : 1);

        short x = MappingMath.ClampToShort(ax * 32767f);
        short y = MappingMath.ClampToShort(ay * 32767f);

        switch (mode)
        {
            case TrackpadMode.AsLeftStick:
                state.ThumbLX = x;
                state.ThumbLY = y;
                break;
            case TrackpadMode.AsRightStick:
                state.ThumbRX = x;
                state.ThumbRY = y;
                break;
            case TrackpadMode.AsDpad:
                const short thresh = 12000;
                if (y > thresh) buttons |= (uint)Xbox360Buttons.DpadUp;
                if (y < -thresh) buttons |= (uint)Xbox360Buttons.DpadDown;
                if (x < -thresh) buttons |= (uint)Xbox360Buttons.DpadLeft;
                if (x > thresh) buttons |= (uint)Xbox360Buttons.DpadRight;
                break;
            case TrackpadMode.AsMouse:
                ApplyRelativePadMouse(id, nx, ny, surface, sensX, sensY, invX, invY, mouseJoystick: false,
                    addMouseJoystick, addMouseDelta);
                break;
            case TrackpadMode.AsMouseJoystick:
                ApplyRelativePadMouse(id, nx, ny, surface, sensX, sensY, invX, invY, mouseJoystick: true,
                    addMouseJoystick, addMouseDelta);
                break;
            case TrackpadMode.FlickStick:
                UpdateFlick(id, ax, ay);
                break;
            case TrackpadMode.ScrollWheel:
                if (!_padMouseLast.TryGetValue(id, out var scrollLast))
                {
                    _padMouseLast[id] = (nx, ny);
                    break;
                }
                _padMouseLast[id] = (nx, ny);
                float scrollDy = -(ny - scrollLast.Y) * sensY * (invY ? -1 : 1);
                // Pad Δ of ~0.1 ≈ one notch.
                _wheelAccum += scrollDy * 1200f;
                break;
            case TrackpadMode.ButtonPad:
                const short bpThresh = 10000;
                if (y > bpThresh) buttons |= (uint)Xbox360Buttons.A;
                if (y < -bpThresh) buttons |= (uint)Xbox360Buttons.Y;
                if (x < -bpThresh) buttons |= (uint)Xbox360Buttons.X;
                if (x > bpThresh) buttons |= (uint)Xbox360Buttons.B;
                break;
        }
    }

    public void TickTrackballCoast(ControllerProfile profile, long mouseJoyLastTick, Action<double, double> addMouseDelta)
    {
        foreach (var id in _padTrackballVel.Keys.ToList())
        {
            // Still touching — velocity was refreshed this frame; don't coast yet.
            if (_padMouseLast.ContainsKey(id))
                continue;

            var surface = id.Equals("LeftTrackpad", StringComparison.OrdinalIgnoreCase)
                ? profile.LeftTrackpadSettings
                : profile.RightTrackpadSettings;
            if (surface is null || !surface.TrackballMode)
            {
                _padTrackballVel.Remove(id);
                continue;
            }

            var mode = id.Equals("LeftTrackpad", StringComparison.OrdinalIgnoreCase)
                ? profile.LeftTrackpad
                : profile.RightTrackpad;
            // Mouse-joystick trackball is return-friction only (see GetMouseJoystickReturnFriction).
            if (mode != TrackpadMode.AsMouse)
            {
                _padTrackballVel.Remove(id);
                continue;
            }

            var (vx, vy) = _padTrackballVel[id];
            float friction = MappingMath.TrackballFrictionPerSec(surface.TrackballFriction);
            float vScale = Math.Clamp(surface.VerticalFrictionScale, 0.1f, 5f);
            float dt = 0.008f;
            long now = Stopwatch.GetTimestamp();
            if (mouseJoyLastTick != 0)
            {
                float measured = (float)(now - mouseJoyLastTick) / Stopwatch.Frequency;
                if (measured > 0f && measured < 0.25f)
                    dt = measured;
            }

            float decayX = MathF.Exp(-friction * dt);
            float decayY = MathF.Exp(-friction * vScale * dt);
            vx *= decayX;
            vy *= decayY;
            if (Math.Abs(vx) < 0.002f && Math.Abs(vy) < 0.002f)
            {
                _padTrackballVel.Remove(id);
                continue;
            }
            _padTrackballVel[id] = (vx, vy);

            addMouseDelta(vx, -vy);
        }
    }

    public void Reset()
    {
        _padMouseLast.Clear();
        _padTrackballVel.Clear();
        _padSmooth.Clear();
        _mouseHapticAccum.Clear();
        _mouseHapticTicks.Clear();
        _flickPads.Clear();
        _wheelAccum = 0;
    }

    void ApplyRelativePadMouse(
        string id,
        float nx,
        float ny,
        TrackpadSurfaceSettings surface,
        float sensX,
        float sensY,
        bool invX,
        bool invY,
        bool mouseJoystick,
        Action<float, float>? addMouseJoystick,
        Action<double, double> addMouseDelta)
    {
        if (!_padMouseLast.TryGetValue(id, out var last))
        {
            _padMouseLast[id] = (nx, ny);
            return;
        }
        _padMouseLast[id] = (nx, ny);

        float dx = nx - last.X;
        float dy = ny - last.Y;
        (dx, dy) = MappingMath.Rotate2D(dx, dy, surface.RotationDegrees);
        // Haptics track raw finger travel so swipe speed maps to tick density.
        float hapticDx = dx;
        float hapticDy = dy;
        dx = SmoothAxis(id, "x", dx, surface.Smoothing);
        dy = SmoothAxis(id, "y", dy, surface.Smoothing);

        float outX = dx * (mouseJoystick ? MouseJoystickPadGain : TrackpadMouseSensitivity) * sensX * (invX ? -1 : 1);
        float outY = dy * (mouseJoystick ? MouseJoystickPadGain : TrackpadMouseSensitivity) * sensY * (invY ? -1 : 1);

        // Impulse coast is only for OS mouse. Mouse-joystick trackball is return-rate only
        // (see GetMouseJoystickReturnFriction) — re-adding flick deltas each coast frame pegs the stick.
        if (surface.TrackballMode && !mouseJoystick)
            UpdateTrackballVelocity(id, outX, outY);

        if (mouseJoystick)
            addMouseJoystick?.Invoke(outX, outY);
        else
            addMouseDelta(outX, -outY);

        ConsiderMouseHaptic(id, hapticDx, hapticDy, surface.MouseHaptics);
    }

    void ClearMouseHapticState(string padId)
    {
        _mouseHapticAccum.Remove(padId);
        if (_mouseHapticTicks.Count == 0) return;

        bool right = padId.Contains("Right", StringComparison.OrdinalIgnoreCase);
        var kept = new Queue<(bool RightPad, byte Intensity)>();
        while (_mouseHapticTicks.Count > 0)
        {
            var tick = _mouseHapticTicks.Dequeue();
            if (tick.RightPad != right)
                kept.Enqueue(tick);
        }
        while (kept.Count > 0)
            _mouseHapticTicks.Enqueue(kept.Dequeue());
    }

    void ConsiderMouseHaptic(string padId, float dx, float dy, MouseHapticsIntensity intensity)
    {
        if (intensity == MouseHapticsIntensity.Off) return;

        float dist = MathF.Sqrt(dx * dx + dy * dy);
        // Pad noise while holding still will otherwise drip into spacing and keep ticking.
        const float idleEpsilon = 0.0025f;
        if (dist < idleEpsilon)
        {
            _mouseHapticAccum.Remove(padId);
            return;
        }

        // Distance-per-tick: faster swipes cross spacing more often → denser clicks.
        float spacing = intensity switch
        {
            MouseHapticsIntensity.Low => 0.055f,
            MouseHapticsIntensity.High => 0.014f,
            _ => 0.028f
        };

        _mouseHapticAccum.TryGetValue(padId, out float accum);
        accum += dist;

        byte level = intensity switch
        {
            MouseHapticsIntensity.Low => 80,
            MouseHapticsIntensity.High => 200,
            _ => 140
        };
        bool right = padId.Contains("Right", StringComparison.OrdinalIgnoreCase);

        // Cap per frame so one HID burst doesn't become a buzz train; leftover
        // distance carries to the next report so continuous motion stays dense.
        int ticks = 0;
        while (accum >= spacing && ticks < 3)
        {
            accum -= spacing;
            _mouseHapticTicks.Enqueue((right, level));
            ticks++;
        }

        _mouseHapticAccum[padId] = accum;
    }

    /// <summary>
    /// Trackball coast must use flick momentum, not the final lift sample.
    /// Pads often jump toward (0,0) on release, which would reverse coast direction.
    /// </summary>
    void UpdateTrackballVelocity(string id, float outX, float outY)
    {
        const float minSample = 0.0008f;
        float mag = MathF.Sqrt(outX * outX + outY * outY);
        if (mag < minSample)
        {
            // Finger nearly still — bleed velocity so a pause before lift doesn't coast.
            if (_padTrackballVel.TryGetValue(id, out var idle))
            {
                idle.Vx *= 0.85f;
                idle.Vy *= 0.85f;
                if (Math.Abs(idle.Vx) < 0.002f && Math.Abs(idle.Vy) < 0.002f)
                    _padTrackballVel.Remove(id);
                else
                    _padTrackballVel[id] = idle;
            }
            return;
        }

        if (!_padTrackballVel.TryGetValue(id, out var prev))
        {
            _padTrackballVel[id] = (outX, outY);
            return;
        }

        float prevMag = MathF.Sqrt(prev.Vx * prev.Vx + prev.Vy * prev.Vy);
        if (prevMag > minSample)
        {
            float dot = (prev.Vx * outX + prev.Vy * outY) / (prevMag * mag);
            // Release spike: large sample opposite the flick — keep prior momentum.
            if (dot < -0.2f && mag > prevMag * 0.35f)
                return;
        }

        // EMA so one noisy frame can't own coast direction.
        const float alpha = 0.45f;
        _padTrackballVel[id] = (
            prev.Vx * (1f - alpha) + outX * alpha,
            prev.Vy * (1f - alpha) + outY * alpha);
    }

    float SmoothAxis(string id, string axis, float sample, float smoothing)
    {
        // Steam-style: higher smoothing → heavier EMA. 0 = pass-through.
        float t = Math.Clamp(smoothing, 0f, 100f) / 100f;
        if (t <= 0.001f) return sample;
        string key = id + ":" + axis;
        float alpha = 1f - t * 0.85f;
        if (!_padSmooth.TryGetValue(key, out var prev))
        {
            _padSmooth[key] = (sample, sample);
            return sample;
        }
        float filtered = prev.X + (sample - prev.X) * alpha;
        _padSmooth[key] = (filtered, filtered);
        return filtered;
    }

    void UpdateFlick(string id, float ax, float ay)
    {
        float mag = MathF.Sqrt(ax * ax + ay * ay);
        if (mag < 0.15f)
            return;

        float angle = MathF.Atan2(ax, ay);
        if (!_flickPads.TryGetValue(id, out var flick))
        {
            _flickPads[id] = new FlickPadState { LastAngle = angle, HasAngle = true };
            return;
        }

        if (!flick.HasAngle)
        {
            flick.LastAngle = angle;
            flick.HasAngle = true;
            return;
        }

        float delta = angle - flick.LastAngle;
        // Wrap to [-π, π]
        while (delta > MathF.PI) delta -= MathF.Tau;
        while (delta < -MathF.PI) delta += MathF.Tau;
        flick.AccumYaw += delta;
        flick.LastAngle = angle;
    }

    void FinishFlick(string id, TrackpadSurfaceSettings surface, Action<double, double> addMouseDelta)
    {
        if (!_flickPads.Remove(id, out var flick) || !flick.HasAngle)
            return;

        // Radians of pad arc → mouse yaw pixels.
        float sens = Math.Clamp(surface.FlickSensitivity, 0.1f, 5f);
        addMouseDelta(flick.AccumYaw * (480f * sens), 0);
    }
}
