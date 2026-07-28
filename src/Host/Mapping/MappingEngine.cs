using SteamControllerBridge.Host.Viiper;
using SteamControllerBridge.Shared;

namespace SteamControllerBridge.Host.Mapping;

public sealed class MappingEngine
{
    double _mouseAccumX;
    double _mouseAccumY;

    public Xbox360InputState Map(SteamControllerState sc, ControllerProfile profile)
    {
        var outState = new Xbox360InputState();
        uint buttons = 0;

        void ApplyDigital(PhysicalInput src, bool pressed)
        {
            if (!pressed) return;
            var dst = profile.MapButton(src);
            buttons |= ToButtonFlag(dst);
        }

        ApplyDigital(PhysicalInput.A, sc.A);
        ApplyDigital(PhysicalInput.B, sc.B);
        ApplyDigital(PhysicalInput.X, sc.X);
        ApplyDigital(PhysicalInput.Y, sc.Y);
        ApplyDigital(PhysicalInput.Lb, sc.Lb);
        ApplyDigital(PhysicalInput.Rb, sc.Rb);
        ApplyDigital(PhysicalInput.View, sc.View);
        ApplyDigital(PhysicalInput.Menu, sc.Menu);
        ApplyDigital(PhysicalInput.Steam, sc.Steam);
        ApplyDigital(PhysicalInput.LsClick, sc.LsClick);
        ApplyDigital(PhysicalInput.RsClick, sc.RsClick);
        ApplyDigital(PhysicalInput.DpadUp, sc.DpadUp);
        ApplyDigital(PhysicalInput.DpadDown, sc.DpadDown);
        ApplyDigital(PhysicalInput.DpadLeft, sc.DpadLeft);
        ApplyDigital(PhysicalInput.DpadRight, sc.DpadRight);
        ApplyDigital(PhysicalInput.L4, sc.L4);
        ApplyDigital(PhysicalInput.L5, sc.L5);
        ApplyDigital(PhysicalInput.R4, sc.R4);
        ApplyDigital(PhysicalInput.R5, sc.R5);
        ApplyDigital(PhysicalInput.LeftTrackpadClick, sc.LeftTrackpadClick);
        ApplyDigital(PhysicalInput.RightTrackpadClick, sc.RightTrackpadClick);

        // Triggers
        byte lt = ScaleTrigger(sc.LeftTrigger, profile.TriggerDeadzone);
        byte rt = ScaleTrigger(sc.RightTrigger, profile.TriggerDeadzone);
        ApplyAnalogTrigger(profile.MapButton(PhysicalInput.Lt), lt, ref outState, ref buttons);
        ApplyAnalogTrigger(profile.MapButton(PhysicalInput.Rt), rt, ref outState, ref buttons);

        // Sticks
        short lsx = ApplyDeadzone(sc.LeftStickX, profile.StickDeadzone);
        short lsy = ApplyDeadzone(sc.LeftStickY, profile.StickDeadzone);
        short rsx = ApplyDeadzone(sc.RightStickX, profile.StickDeadzone);
        short rsy = ApplyDeadzone(sc.RightStickY, profile.StickDeadzone);

        ApplyStick(profile.MapButton(PhysicalInput.LeftStick), lsx, lsy, ref outState);
        ApplyStick(profile.MapButton(PhysicalInput.RightStick), rsx, rsy, ref outState);

        ApplyTrackpad(profile.LeftTrackpad, sc.LeftTrackpadTouch, sc.LeftTrackpadX, sc.LeftTrackpadY, ref outState, ref buttons);
        ApplyTrackpad(profile.RightTrackpad, sc.RightTrackpadTouch, sc.RightTrackpadX, sc.RightTrackpadY, ref outState, ref buttons);

        if (profile.Gyro != GyroMode.Off && sc.HasImu)
            ApplyGyro(profile, sc, ref outState);

        outState.Buttons = buttons;
        return outState;
    }

    static void ApplyAnalogTrigger(XboxOutput dst, byte value, ref Xbox360InputState state, ref uint buttons)
    {
        switch (dst)
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

    static void ApplyStick(XboxOutput dst, short x, short y, ref Xbox360InputState state)
    {
        if (dst == XboxOutput.LeftStick)
        {
            state.ThumbLX = x;
            state.ThumbLY = y;
        }
        else if (dst == XboxOutput.RightStick)
        {
            state.ThumbRX = x;
            state.ThumbRY = y;
        }
    }

    void ApplyTrackpad(TrackpadMode mode, bool touching, short x, short y, ref Xbox360InputState state, ref uint buttons)
    {
        if (!touching || mode == TrackpadMode.Off) return;

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
                // Relative mouse via SendInput — applied by caller through MouseInjector when non-zero deltas accumulate.
                _mouseAccumX += x / 4000.0;
                _mouseAccumY += -y / 4000.0;
                break;
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

    void ApplyGyro(ControllerProfile profile, SteamControllerState sc, ref Xbox360InputState state)
    {
        float s = profile.GyroSensitivity;
        short gx = ClampToShort(sc.GyroY * s); // yaw-ish → stick X
        short gy = ClampToShort(-sc.GyroX * s);
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

    static short ClampToShort(float v) => (short)Math.Clamp((int)v, short.MinValue, short.MaxValue);

    static byte ScaleTrigger(short raw, float deadzone)
    {
        // raw 0..32767
        float n = Math.Clamp(raw / 32767f, 0f, 1f);
        if (n < deadzone) return 0;
        n = (n - deadzone) / (1f - deadzone);
        return (byte)Math.Clamp((int)(n * 255), 0, 255);
    }

    static short ApplyDeadzone(short value, float deadzone)
    {
        float n = value / 32767f;
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
