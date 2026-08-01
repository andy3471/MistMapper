using System.Diagnostics;
using MistMapper.Host.Viiper;
using MistMapper.Shared;

namespace MistMapper.Host.Mapping;

public sealed class MappingEngine
{
    /// <summary>Exponential return-to-center rate (per second) when motion stops.</summary>
    const float MouseJoystickFrictionPerSec = 10f;

    readonly IKeyboardSink _keyboard;
    readonly IMouseSink _mouse;
    readonly TrackpadSurfaceProcessor _trackpad = new();
    readonly GyroProcessor _gyro = new();
    double _mouseAccumX;
    double _mouseAccumY;
    float _mouseJoyX;
    float _mouseJoyY;
    long _mouseJoyLastTick;
    double _wheelAccum;
    readonly HashSet<string> _heldKeys = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> _heldMouse = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, long> _pressStarted = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> _longFired = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> _prevDigitalPressed = new(StringComparer.OrdinalIgnoreCase);

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
            ApplyDigitalBindings(inputId, pressed, profile, ref outState, ref buttons, desiredKeys, desiredMouse);
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

        // Clear press state for digitals no longer reported.
        foreach (var id in _pressStarted.Keys.ToList())
        {
            if (!frame.Digitals.ContainsKey(id) || !frame.GetDigital(id))
            {
                _pressStarted.Remove(id);
                _longFired.Remove(id);
                _prevDigitalPressed.Remove(id);
            }
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

        _trackpad.ApplyTrackpad(profile.LeftTrackpad, frame.GetDigital("LeftTrackpad"), frame, "LeftTrackpad",
            profile.LeftTrackpadSettings, profile, ref outState, ref buttons, AddMouseJoystick, AddMouseDelta);
        _trackpad.ApplyTrackpad(profile.RightTrackpad, frame.GetDigital("RightTrackpad"), frame, "RightTrackpad",
            profile.RightTrackpadSettings, profile, ref outState, ref buttons, AddMouseJoystick, AddMouseDelta);
        _trackpad.TickTrackballCoast(profile, _mouseJoyLastTick, AddMouseDelta);

        if (profile.Gyro != GyroMode.Off
            && _gyro.IsActive(profile, frame)
            && frame.TryGetVector("Gyro", out var gx, out var gy))
            GyroProcessor.Apply(profile, gx, gy, ref outState, AddMouseDelta, AddMouseJoystick);

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

    void AddMouseDelta(double dx, double dy)
    {
        _mouseAccumX += dx;
        _mouseAccumY += dy;
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
                var (frictionX, frictionY) = TrackpadSurfaceProcessor.GetMouseJoystickReturnFriction(
                    profile, frame, MouseJoystickFrictionPerSec);
                _mouseJoyX *= MathF.Exp(-frictionX * dt);
                _mouseJoyY *= MathF.Exp(-frictionY * dt);
            }
        }

        _mouseJoyLastTick = now;
        if (Math.Abs(_mouseJoyX) < 0.01f) _mouseJoyX = 0f;
        if (Math.Abs(_mouseJoyY) < 0.01f) _mouseJoyY = 0f;
    }

    void AddMouseJoystick(float dx, float dy)
    {
        _mouseJoyX = Math.Clamp(_mouseJoyX + dx, -1f, 1f);
        _mouseJoyY = Math.Clamp(_mouseJoyY + dy, -1f, 1f);
    }

    void WriteMouseJoystick(ref Xbox360InputState state)
    {
        short x = MappingMath.ClampToShort(_mouseJoyX * 32767f);
        short y = MappingMath.ClampToShort(_mouseJoyY * 32767f);
        state.ThumbRX = (short)Math.Clamp(state.ThumbRX + x, short.MinValue, short.MaxValue);
        state.ThumbRY = (short)Math.Clamp(state.ThumbRY + y, short.MinValue, short.MaxValue);
    }

    void ResetMouseJoystick()
    {
        _mouseJoyX = 0f;
        _mouseJoyY = 0f;
        _mouseJoyLastTick = 0;
    }

    void ApplyDigitalBindings(
        string inputId,
        bool pressed,
        ControllerProfile profile,
        ref Xbox360InputState state,
        ref uint buttons,
        HashSet<string> desiredKeys,
        HashSet<string> desiredMouse)
    {
        if (!pressed)
        {
            _pressStarted.Remove(inputId);
            _longFired.Remove(inputId);
            _prevDigitalPressed.Remove(inputId);
            return;
        }

        long now = Stopwatch.GetTimestamp();
        bool rising = _prevDigitalPressed.Add(inputId);
        if (!_pressStarted.ContainsKey(inputId))
            _pressStarted[inputId] = now;

        bool hadLong = _longFired.Contains(inputId);
        bool longActive = hadLong;
        if (!longActive)
        {
            double heldMs = (now - _pressStarted[inputId]) * 1000.0 / Stopwatch.Frequency;
            bool hasLong = profile.GetBindings(inputId)
                .Any(b => b.Activator == ActivatorType.LongPress && b.Actions.Any(a => a.Kind != OutputActionKind.None));
            if (hasLong && heldMs >= Math.Max(50, profile.LongPressMs))
            {
                _longFired.Add(inputId);
                longActive = true;
            }
        }
        bool longJustFired = longActive && !hadLong;

        foreach (var binding in profile.GetBindings(inputId))
        {
            if (binding.Activator == ActivatorType.Regular && longActive)
                continue;
            if (binding.Activator == ActivatorType.LongPress && !longActive)
                continue;

            bool edge = rising || (binding.Activator == ActivatorType.LongPress && longJustFired);
            foreach (var action in binding.Actions)
            {
                if (action.Kind == OutputActionKind.None) continue;
                if (IsScrollAction(action))
                {
                    if (edge) AddScrollAction(action);
                    continue;
                }

                ApplyAction(action, 1f, ref state, ref buttons, desiredKeys, desiredMouse);
            }
        }
    }

    static bool IsScrollAction(OutputAction action) =>
        action.Kind == OutputActionKind.MouseButton
        && action.MouseButton is MouseButtonOutput.ScrollUp or MouseButtonOutput.ScrollDown;

    void AddScrollAction(OutputAction action)
    {
        if (action.MouseButton == MouseButtonOutput.ScrollUp)
            _wheelAccum += 120;
        else if (action.MouseButton == MouseButtonOutput.ScrollDown)
            _wheelAccum -= 120;
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
            case OutputActionKind.MouseButton when IsScrollAction(action):
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

    /// <summary>Drain one pending Steam-style mouse haptic tick (if any).</summary>
    public bool TryConsumeMouseHaptic(out bool rightPad, out byte intensity) =>
        _trackpad.TryConsumeMouseHaptic(out rightPad, out intensity);

    public bool TryConsumeMouseDelta(out int dx, out int dy)
    {
        dx = (int)_mouseAccumX;
        dy = (int)_mouseAccumY;
        if (dx == 0 && dy == 0) return false;
        _mouseAccumX -= dx;
        _mouseAccumY -= dy;
        return true;
    }

    public bool TryConsumeMouseWheel(out int wheelDelta)
    {
        double total = _wheelAccum + _trackpad.WheelAccum;
        if (Math.Abs(total) < 120)
        {
            wheelDelta = 0;
            return false;
        }

        wheelDelta = (int)(total / 120) * 120;
        double remaining = wheelDelta;
        if (_wheelAccum != 0 && Math.Sign(_wheelAccum) == Math.Sign(remaining))
        {
            double take = Math.Min(Math.Abs(_wheelAccum), Math.Abs(remaining)) * Math.Sign(remaining);
            _wheelAccum -= take;
            remaining -= take;
        }
        if (remaining != 0)
            _trackpad.AddWheelDelta(-remaining);
        return true;
    }

    public void ReleaseAllInjected()
    {
        ResetMouseJoystick();
        _trackpad.Reset();
        _pressStarted.Clear();
        _longFired.Clear();
        _prevDigitalPressed.Clear();
        _wheelAccum = 0;
        _gyro.Reset();
        foreach (var token in _heldKeys.ToList())
            ReleaseKeyToken(token);
        _heldKeys.Clear();
        foreach (var btn in _heldMouse.ToList())
        {
            if (Enum.TryParse<MouseButtonOutput>(btn, true, out var b)
                && b is not (MouseButtonOutput.ScrollUp or MouseButtonOutput.ScrollDown))
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
            if (b is MouseButtonOutput.ScrollUp or MouseButtonOutput.ScrollDown) continue;
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
        return MappingMath.ClampToShort(v);
    }

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
