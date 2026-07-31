using MistMapper.Host.Viiper;
using MistMapper.Shared;

namespace MistMapper.Host.Mapping;

sealed class GyroProcessor
{
    /// <summary>How strongly normalized gyro rates push the virtual stick tip.</summary>
    const float MouseJoystickGyroGain = 2.2f;

    bool _gyroToggleOn;
    bool _gyroTogglePrevPressed;

    public bool IsActive(ControllerProfile profile, InputFrame frame)
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

    public static void Apply(
        ControllerProfile profile,
        float ngx,
        float ngy,
        ref Xbox360InputState state,
        Action<double, double> addMouseDelta,
        Action<float, float> addMouseJoystick)
    {
        float calib = Math.Clamp(profile.GyroDotsPer360, 500f, 20000f) / 6545f;
        float sx = profile.GyroSensitivityX * profile.GyroSensitivity * calib;
        float sy = profile.GyroSensitivityY * profile.GyroSensitivity * calib;
        short gx = MappingMath.ClampToShort(ngy * 32767f * sx * (profile.InvertGyroX ? -1 : 1));
        short gy = MappingMath.ClampToShort(-ngx * 32767f * sy * (profile.InvertGyroY ? -1 : 1));
        if (profile.Gyro == GyroMode.AsRightStick)
        {
            state.ThumbRX = (short)Math.Clamp(state.ThumbRX + gx, short.MinValue, short.MaxValue);
            state.ThumbRY = (short)Math.Clamp(state.ThumbRY + gy, short.MinValue, short.MaxValue);
        }
        else if (profile.Gyro == GyroMode.AsMouse)
        {
            addMouseDelta(gx / 2000.0, -gy / 2000.0);
        }
        else if (profile.Gyro == GyroMode.AsMouseJoystick)
        {
            addMouseJoystick(
                ngy * MouseJoystickGyroGain * sx * (profile.InvertGyroX ? -1 : 1),
                -ngx * MouseJoystickGyroGain * sy * (profile.InvertGyroY ? -1 : 1));
        }
    }

    public void Reset()
    {
        _gyroToggleOn = false;
        _gyroTogglePrevPressed = false;
    }
}
