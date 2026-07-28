using SteamControllerBridge.Host.Viiper;
using SteamControllerBridge.Shared;

namespace SteamControllerBridge.Host.Mapping;

public sealed class MappingEngine
{
    /// <summary>Pixels of cursor travel for a full-pad finger swipe (normalized Δ≈2).</summary>
    const float TrackpadMouseSensitivity = 900f;

    double _mouseAccumX;
    double _mouseAccumY;
    readonly Dictionary<string, (float X, float Y)> _padMouseLast = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> _heldKeys = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> _heldMouse = new(StringComparer.OrdinalIgnoreCase);

    public Xbox360InputState Map(InputFrame frame, ControllerProfile profile)
    {
        var outState = new Xbox360InputState();
        uint buttons = 0;
        var desiredKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var desiredMouse = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void ApplyDigital(string inputId, bool pressed)
        {
            if (!pressed) return;
            ApplyAction(profile.GetAction(inputId), 1f, ref outState, ref buttons, desiredKeys, desiredMouse);
        }

        foreach (var (id, pressed) in frame.Digitals)
        {
            // Trackpads themselves are mode-driven; clicks are remappable digitals.
            if (id is "LeftTrackpad" or "RightTrackpad" or "Gyro") continue;

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
            ApplyStick(profile.GetAction("LeftStick"), ApplyDeadzone(lsx, profile.StickDeadzone), ApplyDeadzone(lsy, profile.StickDeadzone), ref outState);
        if (frame.TryGetVector("RightStick", out var rsx, out var rsy))
            ApplyStick(profile.GetAction("RightStick"), ApplyDeadzone(rsx, profile.StickDeadzone), ApplyDeadzone(rsy, profile.StickDeadzone), ref outState);

        ApplyTrackpad(profile.LeftTrackpad, frame.GetDigital("LeftTrackpad"), frame, "LeftTrackpad", ref outState, ref buttons);
        ApplyTrackpad(profile.RightTrackpad, frame.GetDigital("RightTrackpad"), frame, "RightTrackpad", ref outState, ref buttons);

        if (profile.Gyro != GyroMode.Off && frame.TryGetVector("Gyro", out var gx, out var gy))
            ApplyGyro(profile, gx, gy, ref outState);

        SyncKeys(desiredKeys);
        SyncMouse(desiredMouse);

        outState.Buttons = buttons;
        return outState;
    }

    void ApplyAction(
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

    void ApplyAnalogTrigger(
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

    void ApplyTrackpad(TrackpadMode mode, bool touching, InputFrame frame, string id, ref Xbox360InputState state, ref uint buttons)
    {
        if (mode == TrackpadMode.Off) return;
        if (!touching)
        {
            _padMouseLast.Remove(id);
            return;
        }

        if (!frame.TryGetVector(id, out var nx, out var ny)) return;
        short x = (short)Math.Clamp((int)(nx * 32767), short.MinValue, short.MaxValue);
        short y = (short)Math.Clamp((int)(ny * 32767), short.MinValue, short.MaxValue);

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
                // Relative finger motion (Steam Desktop-style), not absolute position-as-velocity.
                if (!_padMouseLast.TryGetValue(id, out var last))
                {
                    _padMouseLast[id] = (nx, ny);
                    break;
                }

                _padMouseLast[id] = (nx, ny);
                _mouseAccumX += (nx - last.X) * TrackpadMouseSensitivity;
                _mouseAccumY += -(ny - last.Y) * TrackpadMouseSensitivity;
                break;
        }
    }

    void ApplyGyro(ControllerProfile profile, float ngx, float ngy, ref Xbox360InputState state)
    {
        float s = profile.GyroSensitivity;
        short gx = ClampToShort(ngy * 32767f * s);
        short gy = ClampToShort(-ngx * 32767f * s);
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
        foreach (var token in _heldKeys.ToList())
            ReleaseKeyToken(token);
        _heldKeys.Clear();
        foreach (var btn in _heldMouse.ToList())
        {
            if (Enum.TryParse<MouseButtonOutput>(btn, true, out var b))
                MouseInjector.SetButton(b, false);
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
            KeyboardInjector.SetModifier(mods, true);
            KeyboardInjector.SetKey(vk, true);
            _heldKeys.Add(token);
        }
    }

    void SyncMouse(HashSet<string> desired)
    {
        foreach (var btn in _heldMouse.Where(b => !desired.Contains(b)).ToList())
        {
            if (Enum.TryParse<MouseButtonOutput>(btn, true, out var b))
                MouseInjector.SetButton(b, false);
            _heldMouse.Remove(btn);
        }
        foreach (var btn in desired)
        {
            if (_heldMouse.Contains(btn)) continue;
            if (!Enum.TryParse<MouseButtonOutput>(btn, true, out var b)) continue;
            MouseInjector.SetButton(b, true);
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

    static void ReleaseKeyToken(string token)
    {
        if (!TryParseKeyToken(token, out var mods, out var vk)) return;
        KeyboardInjector.SetKey(vk, false);
        KeyboardInjector.SetModifier(mods, false);
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
