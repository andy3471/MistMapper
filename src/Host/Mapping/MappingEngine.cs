using System.Diagnostics;
using MistMapper.Host.Viiper;
using MistMapper.Shared;

namespace MistMapper.Host.Mapping;

public sealed class MappingEngine
{
    /// <summary>Pixels of cursor travel for a full-pad finger swipe (normalized Δ≈2).</summary>
    const float TrackpadMouseSensitivity = 900f;

    /// <summary>Pad Δ≈0.2 at gain 5 → full stick tip (Steam-like mouse→stick).</summary>
    const float MouseJoystickPadGain = 5f;

    /// <summary>How strongly normalized gyro rates push the virtual stick tip.</summary>
    const float MouseJoystickGyroGain = 2.2f;

    /// <summary>Exponential return-to-center rate (per second) when motion stops.</summary>
    const float MouseJoystickFrictionPerSec = 10f;

    readonly IKeyboardSink _keyboard;
    readonly IMouseSink _mouse;
    double _mouseAccumX;
    double _mouseAccumY;
    float _mouseJoyX;
    float _mouseJoyY;
    long _mouseJoyLastTick;
    readonly Dictionary<string, (float X, float Y)> _padMouseLast = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, (float Vx, float Vy)> _padTrackballVel = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, (float X, float Y)> _padSmooth = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, float> _mouseHapticAccum = new(StringComparer.OrdinalIgnoreCase);
    readonly Queue<(bool RightPad, byte Intensity)> _mouseHapticTicks = new();
    readonly HashSet<string> _heldKeys = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> _heldMouse = new(StringComparer.OrdinalIgnoreCase);
    bool _gyroToggleOn;
    bool _gyroTogglePrevPressed;

    public MappingEngine(IKeyboardSink? keyboard = null, IMouseSink? mouse = null)
    {
        _keyboard = keyboard ?? Win32KeyboardSink.Instance;
        _mouse = mouse ?? Win32MouseSink.Instance;
    }

    /// <param name="allowKeyboardMouse">
    /// When false, Xbox mapping still runs but OS keyboard/mouse inject is skipped
    /// (non-primary pads in multi-controller mode).
    /// </param>
    public Xbox360InputState Map(InputFrame frame, ControllerProfile profile, bool allowKeyboardMouse = true)
    {
        var outState = new Xbox360InputState();
        uint buttons = 0;
        var desiredKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var desiredMouse = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var mouseJoyActive = UsesMouseJoystick(profile);
        if (mouseJoyActive)
            DecayMouseJoystick(profile, frame);
        else
            ResetMouseJoystick();

        void ApplyDigital(string inputId, bool pressed)
        {
            if (!pressed) return;
            ApplyAction(profile.GetAction(inputId), 1f, ref outState, ref buttons, desiredKeys, desiredMouse);
        }

        foreach (var (id, pressed) in frame.Digitals)
        {
            // Mode-driven / capacitive gates — not remappable digitals.
            if (id is "LeftTrackpad" or "RightTrackpad" or "Gyro"
                or "LeftStickTouch" or "RightStickTouch")
                continue;

            // Steam / Guide is permanently locked to Xbox Guide (profile remap ignored).
            if (MappingLocks.IsLockedGuideInput(id))
            {
                if (pressed) buttons |= (uint)Xbox360Buttons.Guide;
                continue;
            }

            ApplyDigital(id, pressed);
        }

        ApplyAnalogTrigger(profile.GetAction("Lt"), ScaleAnalog(frame.GetAnalog("Lt"), profile.TriggerDeadzone), ref outState, ref buttons, desiredKeys, desiredMouse);
        ApplyAnalogTrigger(profile.GetAction("Rt"), ScaleAnalog(frame.GetAnalog("Rt"), profile.TriggerDeadzone), ref outState, ref buttons, desiredKeys, desiredMouse);

        if (frame.TryGetVector("LeftStick", out var lsx, out var lsy))
        {
            var sx = ApplyDeadzone(lsx, profile.StickDeadzone);
            var sy = ApplyDeadzone(lsy, profile.StickDeadzone);
            sx = ApplySensitivityShort(sx, profile.StickSensitivityX, profile.InvertStickX);
            sy = ApplySensitivityShort(sy, profile.StickSensitivityY, profile.InvertStickY);
            ApplyStick(profile.GetAction("LeftStick"), sx, sy, ref outState);
        }
        if (frame.TryGetVector("RightStick", out var rsx, out var rsy))
        {
            var sx = ApplyDeadzone(rsx, profile.StickDeadzone);
            var sy = ApplyDeadzone(rsy, profile.StickDeadzone);
            sx = ApplySensitivityShort(sx, profile.StickSensitivityX, profile.InvertStickX);
            sy = ApplySensitivityShort(sy, profile.StickSensitivityY, profile.InvertStickY);
            ApplyStick(profile.GetAction("RightStick"), sx, sy, ref outState);
        }

        ApplyTrackpad(profile.LeftTrackpad, frame.GetDigital("LeftTrackpad"), frame, "LeftTrackpad",
            profile.LeftTrackpadSettings, ref outState, ref buttons, profile);
        ApplyTrackpad(profile.RightTrackpad, frame.GetDigital("RightTrackpad"), frame, "RightTrackpad",
            profile.RightTrackpadSettings, ref outState, ref buttons, profile);
        TickTrackballCoast(profile);

        if (profile.Gyro != GyroMode.Off
            && IsGyroActive(profile, frame)
            && frame.TryGetVector("Gyro", out var gx, out var gy))
            ApplyGyro(profile, gx, gy, ref outState);

        if (mouseJoyActive)
            WriteMouseJoystick(ref outState);

        if (allowKeyboardMouse)
        {
            SyncKeys(desiredKeys);
            SyncMouse(desiredMouse);
        }
        else
        {
            // Drop any held inject from this engine if it lost KB/mouse privilege.
            if (_heldKeys.Count > 0 || _heldMouse.Count > 0)
                ReleaseAllInjected();
        }

        outState.Buttons = buttons;
        return outState;
    }

    static bool UsesMouseJoystick(ControllerProfile profile) =>
        profile.LeftTrackpad == TrackpadMode.AsMouseJoystick
        || profile.RightTrackpad == TrackpadMode.AsMouseJoystick
        || profile.Gyro == GyroMode.AsMouseJoystick;

    void DecayMouseJoystick(ControllerProfile profile, InputFrame frame)
    {
        long now = Stopwatch.GetTimestamp();
        if (_mouseJoyLastTick != 0)
        {
            float dt = (float)(now - _mouseJoyLastTick) / Stopwatch.Frequency;
            if (dt > 0f && dt < 0.25f)
            {
                float friction = MouseJoystickReturnFriction(profile, frame);
                float decay = MathF.Exp(-friction * dt);
                _mouseJoyX *= decay;
                _mouseJoyY *= decay;
            }
        }

        _mouseJoyLastTick = now;
        if (Math.Abs(_mouseJoyX) < 0.01f) _mouseJoyX = 0f;
        if (Math.Abs(_mouseJoyY) < 0.01f) _mouseJoyY = 0f;
    }

    /// <summary>
    /// Mouse-joystick "trackball" = slower return-to-center after a flick, not a
    /// per-frame impulse loop (that pegs the stick and spins the camera).
    /// </summary>
    static float MouseJoystickReturnFriction(ControllerProfile profile, InputFrame frame)
    {
        static float FromPad(TrackpadMode mode, TrackpadSurfaceSettings? settings, bool touching)
        {
            if (mode != TrackpadMode.AsMouseJoystick || settings is not { TrackballMode: true })
                return -1f;
            // While still touching, keep a snappier return so resting on the pad centers.
            if (touching)
                return Math.Max(MouseJoystickFrictionPerSec, MouseJoystickTrackballReturnPerSec(settings.TrackballFriction));
            return MouseJoystickTrackballReturnPerSec(settings.TrackballFriction);
        }

        float left = FromPad(profile.LeftTrackpad, profile.LeftTrackpadSettings, frame.GetDigital("LeftTrackpad"));
        float right = FromPad(profile.RightTrackpad, profile.RightTrackpadSettings, frame.GetDigital("RightTrackpad"));
        if (left < 0 && right < 0)
            return MouseJoystickFrictionPerSec;
        if (left < 0) return right;
        if (right < 0) return left;
        return Math.Min(left, right);
    }

    void AddMouseJoystick(float dx, float dy)
    {
        _mouseJoyX = Math.Clamp(_mouseJoyX + dx, -1f, 1f);
        _mouseJoyY = Math.Clamp(_mouseJoyY + dy, -1f, 1f);
    }

    void WriteMouseJoystick(ref Xbox360InputState state)
    {
        short x = ClampToShort(_mouseJoyX * 32767f);
        short y = ClampToShort(_mouseJoyY * 32767f);
        state.ThumbRX = (short)Math.Clamp(state.ThumbRX + x, short.MinValue, short.MaxValue);
        state.ThumbRY = (short)Math.Clamp(state.ThumbRY + y, short.MinValue, short.MaxValue);
    }

    void ResetMouseJoystick()
    {
        _mouseJoyX = 0f;
        _mouseJoyY = 0f;
        _mouseJoyLastTick = 0;
    }

    static void ApplyAction(
        OutputAction action,
        float strength,
        ref Xbox360InputState state,
        ref uint buttons,
        HashSet<string> desiredKeys,
        HashSet<string> desiredMouse)
    {
        switch (action.Kind)
        {
            case OutputActionKind.Xbox:
                if (action.Xbox is XboxOutput.Lt or XboxOutput.Rt)
                    ApplyAnalogTrigger(action, (byte)Math.Clamp((int)(strength * 255), 0, 255), ref state, ref buttons, desiredKeys, desiredMouse);
                else if (action.Xbox is XboxOutput.LeftStick or XboxOutput.RightStick)
                {
                    // digital → stick not supported as force; ignore
                }
                else
                    buttons |= ToButtonFlag(action.Xbox);
                break;
            case OutputActionKind.Key when action.VirtualKey > 0:
                desiredKeys.Add(KeyToken(action));
                break;
            case OutputActionKind.MouseButton:
                desiredMouse.Add(action.MouseButton.ToString());
                break;
        }
    }

    static void ApplyAnalogTrigger(
        OutputAction action,
        byte value,
        ref Xbox360InputState state,
        ref uint buttons,
        HashSet<string> desiredKeys,
        HashSet<string> desiredMouse)
    {
        if (action.Kind == OutputActionKind.Key && value > 32)
        {
            desiredKeys.Add(KeyToken(action));
            return;
        }
        if (action.Kind == OutputActionKind.MouseButton && value > 32)
        {
            desiredMouse.Add(action.MouseButton.ToString());
            return;
        }
        if (action.Kind != OutputActionKind.Xbox) return;

        switch (action.Xbox)
        {
            case XboxOutput.Lt: state.LeftTrigger = Math.Max(state.LeftTrigger, value); break;
            case XboxOutput.Rt: state.RightTrigger = Math.Max(state.RightTrigger, value); break;
            case XboxOutput.A when value > 32: buttons |= (uint)Xbox360Buttons.A; break;
            case XboxOutput.B when value > 32: buttons |= (uint)Xbox360Buttons.B; break;
            case XboxOutput.X when value > 32: buttons |= (uint)Xbox360Buttons.X; break;
            case XboxOutput.Y when value > 32: buttons |= (uint)Xbox360Buttons.Y; break;
            case XboxOutput.Lb when value > 32: buttons |= (uint)Xbox360Buttons.LeftShoulder; break;
            case XboxOutput.Rb when value > 32: buttons |= (uint)Xbox360Buttons.RightShoulder; break;
        }
    }

    static void ApplyStick(OutputAction action, short x, short y, ref Xbox360InputState state)
    {
        if (action.Kind != OutputActionKind.Xbox) return;
        if (action.Xbox == XboxOutput.LeftStick)
        {
            state.ThumbLX = x;
            state.ThumbLY = y;
        }
        else if (action.Xbox == XboxOutput.RightStick)
        {
            state.ThumbRX = x;
            state.ThumbRY = y;
        }
    }

    void ApplyTrackpad(TrackpadMode mode, bool touching, InputFrame frame, string id,
        TrackpadSurfaceSettings? surface, ref Xbox360InputState state, ref uint buttons, ControllerProfile profile)
    {
        surface ??= new TrackpadSurfaceSettings();
        if (mode == TrackpadMode.Off) return;

        if (!touching)
        {
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
        (ax, ay) = Rotate2D(ax, ay, surface.RotationDegrees);
        ax *= sensX * (invX ? -1 : 1);
        ay *= sensY * (invY ? -1 : 1);

        short x = ClampToShort(ax * 32767f);
        short y = ClampToShort(ay * 32767f);

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
                ApplyRelativePadMouse(id, nx, ny, surface, sensX, sensY, invX, invY, mouseJoystick: false);
                break;
            case TrackpadMode.AsMouseJoystick:
                ApplyRelativePadMouse(id, nx, ny, surface, sensX, sensY, invX, invY, mouseJoystick: true);
                break;
            case TrackpadMode.FlickStick:
                if (Math.Abs(ax) > 0.5f || Math.Abs(ay) > 0.5f)
                {
                    float angle = MathF.Atan2(ax, ay);
                    state.ThumbRX = ClampToShort(MathF.Sin(angle) * 32767f);
                    state.ThumbRY = ClampToShort(MathF.Cos(angle) * 32767f);
                }
                break;
            case TrackpadMode.ScrollWheel:
                if (!_padMouseLast.TryGetValue(id, out var scrollLast))
                {
                    _padMouseLast[id] = (nx, ny);
                    break;
                }
                _padMouseLast[id] = (nx, ny);
                _mouseAccumY += -(ny - scrollLast.Y) * 120f * sensY * (invY ? -1 : 1);
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

    void ApplyRelativePadMouse(string id, float nx, float ny, TrackpadSurfaceSettings surface,
        float sensX, float sensY, bool invX, bool invY, bool mouseJoystick)
    {
        if (!_padMouseLast.TryGetValue(id, out var last))
        {
            _padMouseLast[id] = (nx, ny);
            return;
        }
        _padMouseLast[id] = (nx, ny);

        float dx = nx - last.X;
        float dy = ny - last.Y;
        (dx, dy) = Rotate2D(dx, dy, surface.RotationDegrees);
        // Haptics track raw finger travel so swipe speed maps to tick density.
        float hapticDx = dx;
        float hapticDy = dy;
        dx = SmoothAxis(id, "x", dx, surface.Smoothing);
        dy = SmoothAxis(id, "y", dy, surface.Smoothing);

        float outX = dx * (mouseJoystick ? MouseJoystickPadGain : TrackpadMouseSensitivity) * sensX * (invX ? -1 : 1);
        float outY = dy * (mouseJoystick ? MouseJoystickPadGain : TrackpadMouseSensitivity) * sensY * (invY ? -1 : 1);

        // Impulse coast is only for OS mouse. Mouse-joystick trackball is return-rate only
        // (see MouseJoystickReturnFriction) — re-adding flick deltas each coast frame pegs the stick.
        if (surface.TrackballMode && !mouseJoystick)
            UpdateTrackballVelocity(id, outX, outY);

        if (mouseJoystick)
            AddMouseJoystick(outX, outY);
        else
        {
            _mouseAccumX += outX;
            _mouseAccumY += -outY;
        }

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
        if (dist < 0.00002f) return;

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

    /// <summary>Drain one pending Steam-style mouse haptic tick (if any).</summary>
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

    void TickTrackballCoast(ControllerProfile profile)
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
            // Mouse-joystick trackball is return-friction only (see MouseJoystickReturnFriction).
            if (mode != TrackpadMode.AsMouse)
            {
                _padTrackballVel.Remove(id);
                continue;
            }

            var (vx, vy) = _padTrackballVel[id];
            float friction = TrackballFrictionPerSec(surface.TrackballFriction);
            float vScale = Math.Clamp(surface.VerticalFrictionScale, 0.1f, 5f);
            float dt = 0.008f;
            long now = Stopwatch.GetTimestamp();
            if (_mouseJoyLastTick != 0)
            {
                float measured = (float)(now - _mouseJoyLastTick) / Stopwatch.Frequency;
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

            _mouseAccumX += vx;
            _mouseAccumY += -vy;
        }
    }

    static float TrackballFrictionPerSec(TrackballFriction f) => f switch
    {
        TrackballFriction.Off => 0.8f,
        TrackballFriction.Low => 2f,
        TrackballFriction.Medium => 5f,
        TrackballFriction.High => 10f,
        TrackballFriction.ExtraHigh => 16f,
        _ => 5f
    };

    /// <summary>Return-to-center rates for mouse-joystick trackball (lower = longer linger).</summary>
    static float MouseJoystickTrackballReturnPerSec(TrackballFriction f) => f switch
    {
        TrackballFriction.Off => 0.5f,
        TrackballFriction.Low => 1.0f,
        TrackballFriction.Medium => 2.2f,
        TrackballFriction.High => 5.0f,
        TrackballFriction.ExtraHigh => 9.0f,
        _ => 2.2f
    };

    static (float X, float Y) Rotate2D(float x, float y, float degrees)
    {
        if (Math.Abs(degrees) < 0.01f) return (x, y);
        float rad = degrees * (MathF.PI / 180f);
        float c = MathF.Cos(rad);
        float s = MathF.Sin(rad);
        return (x * c - y * s, x * s + y * c);
    }

    bool IsGyroActive(ControllerProfile profile, InputFrame frame)
    {
        var buttons = profile.GyroButtons;
        if (buttons is null || buttons.Count == 0)
            return true; // Steam: no buttons selected → always on

        bool pressed = profile.GyroButtonCombine == GyroButtonCombine.All
            ? buttons.All(id => frame.GetDigital(id))
            : buttons.Any(id => frame.GetDigital(id));

        switch (profile.GyroButtonMode)
        {
            case GyroButtonMode.HoldToEnable:
                return pressed;
            case GyroButtonMode.HoldToSuppress:
                return !pressed;
            case GyroButtonMode.Toggle:
                if (pressed && !_gyroTogglePrevPressed)
                    _gyroToggleOn = !_gyroToggleOn;
                _gyroTogglePrevPressed = pressed;
                return _gyroToggleOn;
            default:
                return true;
        }
    }

    void ApplyGyro(ControllerProfile profile, float ngx, float ngy, ref Xbox360InputState state)
    {
        float calib = Math.Clamp(profile.GyroDotsPer360, 500f, 20000f) / 6545f;
        float sx = profile.GyroSensitivityX * profile.GyroSensitivity * calib;
        float sy = profile.GyroSensitivityY * profile.GyroSensitivity * calib;
        short gx = ClampToShort(ngy * 32767f * sx * (profile.InvertGyroX ? -1 : 1));
        short gy = ClampToShort(-ngx * 32767f * sy * (profile.InvertGyroY ? -1 : 1));
        if (profile.Gyro == GyroMode.AsRightStick)
        {
            state.ThumbRX = (short)Math.Clamp(state.ThumbRX + gx, short.MinValue, short.MaxValue);
            state.ThumbRY = (short)Math.Clamp(state.ThumbRY + gy, short.MinValue, short.MaxValue);
        }
        else if (profile.Gyro == GyroMode.AsMouse)
        {
            _mouseAccumX += gx / 2000.0;
            _mouseAccumY += -gy / 2000.0;
        }
        else if (profile.Gyro == GyroMode.AsMouseJoystick)
        {
            AddMouseJoystick(
                ngy * MouseJoystickGyroGain * sx * (profile.InvertGyroX ? -1 : 1),
                -ngx * MouseJoystickGyroGain * sy * (profile.InvertGyroY ? -1 : 1));
        }
    }

    public bool TryConsumeMouseDelta(out int dx, out int dy)
    {
        dx = (int)_mouseAccumX;
        dy = (int)_mouseAccumY;
        if (dx == 0 && dy == 0) return false;
        _mouseAccumX -= dx;
        _mouseAccumY -= dy;
        return true;
    }

    public void ReleaseAllInjected()
    {
        ResetMouseJoystick();
        _padMouseLast.Clear();
        _padTrackballVel.Clear();
        _padSmooth.Clear();
        _mouseHapticAccum.Clear();
        _mouseHapticTicks.Clear();
        _gyroToggleOn = false;
        _gyroTogglePrevPressed = false;
        foreach (var token in _heldKeys.ToList())
            ReleaseKeyToken(token);
        _heldKeys.Clear();
        foreach (var btn in _heldMouse.ToList())
        {
            if (Enum.TryParse<MouseButtonOutput>(btn, true, out var b))
                _mouse.SetButton(b, false);
        }
        _heldMouse.Clear();
    }

    void SyncKeys(HashSet<string> desired)
    {
        foreach (var token in _heldKeys.Where(t => !desired.Contains(t)).ToList())
        {
            ReleaseKeyToken(token);
            _heldKeys.Remove(token);
        }
        foreach (var token in desired)
        {
            if (_heldKeys.Contains(token)) continue;
            if (!TryParseKeyToken(token, out var mods, out var vk)) continue;
            _keyboard.SetModifier(mods, true);
            _keyboard.SetKey(vk, true);
            _heldKeys.Add(token);
        }
    }

    void SyncMouse(HashSet<string> desired)
    {
        foreach (var btn in _heldMouse.Where(b => !desired.Contains(b)).ToList())
        {
            if (Enum.TryParse<MouseButtonOutput>(btn, true, out var b))
                _mouse.SetButton(b, false);
            _heldMouse.Remove(btn);
        }
        foreach (var btn in desired)
        {
            if (_heldMouse.Contains(btn)) continue;
            if (!Enum.TryParse<MouseButtonOutput>(btn, true, out var b)) continue;
            _mouse.SetButton(b, true);
            _heldMouse.Add(btn);
        }
    }

    static string KeyToken(OutputAction a) => $"{(int)a.Modifiers}:{a.VirtualKey}";

    static bool TryParseKeyToken(string token, out KeyModifiers mods, out int vk)
    {
        mods = KeyModifiers.None;
        vk = 0;
        var parts = token.Split(':');
        if (parts.Length != 2) return false;
        if (!int.TryParse(parts[0], out var m)) return false;
        if (!int.TryParse(parts[1], out vk)) return false;
        mods = (KeyModifiers)m;
        return true;
    }

    void ReleaseKeyToken(string token)
    {
        if (!TryParseKeyToken(token, out var mods, out var vk)) return;
        _keyboard.SetKey(vk, false);
        _keyboard.SetModifier(mods, false);
    }

    static short ApplySensitivityShort(short value, float sensitivity, bool invert)
    {
        float v = value * sensitivity * (invert ? -1 : 1);
        return ClampToShort(v);
    }

    static short ClampToShort(float v) => (short)Math.Clamp((int)v, short.MinValue, short.MaxValue);

    static byte ScaleAnalog(float n, float deadzone)
    {
        n = Math.Clamp(n, 0f, 1f);
        if (n < deadzone) return 0;
        n = (n - deadzone) / (1f - deadzone);
        return (byte)Math.Clamp((int)(n * 255), 0, 255);
    }

    static short ApplyDeadzone(float n, float deadzone)
    {
        float mag = Math.Abs(n);
        if (mag < deadzone) return 0;
        float sign = Math.Sign(n);
        float scaled = (mag - deadzone) / (1f - deadzone) * sign;
        return (short)Math.Clamp((int)(scaled * 32767), short.MinValue, short.MaxValue);
    }

    public static uint ToButtonFlag(XboxOutput output) => output switch
    {
        XboxOutput.A => (uint)Xbox360Buttons.A,
        XboxOutput.B => (uint)Xbox360Buttons.B,
        XboxOutput.X => (uint)Xbox360Buttons.X,
        XboxOutput.Y => (uint)Xbox360Buttons.Y,
        XboxOutput.Lb => (uint)Xbox360Buttons.LeftShoulder,
        XboxOutput.Rb => (uint)Xbox360Buttons.RightShoulder,
        XboxOutput.Back => (uint)Xbox360Buttons.Back,
        XboxOutput.Start => (uint)Xbox360Buttons.Start,
        XboxOutput.Guide => (uint)Xbox360Buttons.Guide,
        XboxOutput.LsClick => (uint)Xbox360Buttons.LeftThumb,
        XboxOutput.RsClick => (uint)Xbox360Buttons.RightThumb,
        XboxOutput.DpadUp => (uint)Xbox360Buttons.DpadUp,
        XboxOutput.DpadDown => (uint)Xbox360Buttons.DpadDown,
        XboxOutput.DpadLeft => (uint)Xbox360Buttons.DpadLeft,
        XboxOutput.DpadRight => (uint)Xbox360Buttons.DpadRight,
        _ => 0
    };
}
