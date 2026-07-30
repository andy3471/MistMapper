namespace MistMapper.Shared;

/// <summary>Physical Steam Controller 2026 inputs that can be remapped.</summary>
public enum PhysicalInput
{
    A,
    B,
    X,
    Y,
    Lb,
    Rb,
    View,
    Menu,
    Steam,
    LsClick,
    RsClick,
    DpadUp,
    DpadDown,
    DpadLeft,
    DpadRight,
    L4,
    L5,
    R4,
    R5,
    Lt,
    Rt,
    LeftStick,
    RightStick,
    LeftTrackpad,
    RightTrackpad,
    LeftTrackpadClick,
    RightTrackpadClick,
    /// <summary>Capacitive touch on the left stick (not click).</summary>
    LeftStickTouch,
    /// <summary>Capacitive touch on the right stick (not click).</summary>
    RightStickTouch,
    Gyro
}

/// <summary>Virtual Xbox 360 outputs.</summary>
public enum XboxOutput
{
    None,
    A,
    B,
    X,
    Y,
    Lb,
    Rb,
    Back,
    Start,
    Guide,
    LsClick,
    RsClick,
    DpadUp,
    DpadDown,
    DpadLeft,
    DpadRight,
    Lt,
    Rt,
    LeftStick,
    RightStick
}

public enum TrackpadMode
{
    Off,
    AsMouse,
    AsLeftStick,
    AsRightStick,
    AsDpad,
    FlickStick,
    ScrollWheel,
    ButtonPad,
    /// <summary>Relative pad motion → virtual stick (Steam “Mouse Joystick”).</summary>
    AsMouseJoystick
}

public enum GyroMode
{
    Off,
    AsRightStick,
    AsMouse,
    /// <summary>Gyro rates → virtual stick (Steam “Mouse Joystick” layer).</summary>
    AsMouseJoystick
}

/// <summary>How selected gyro buttons interact with gyro output (Steam Input).</summary>
public enum GyroButtonMode
{
    /// <summary>Gyro runs only while any/all selected buttons are held.</summary>
    HoldToEnable,
    /// <summary>Gyro runs by default; holding buttons suppresses it.</summary>
    HoldToSuppress,
    /// <summary>Press toggles gyro on/off.</summary>
    Toggle
}

/// <summary>How multiple gyro activation buttons combine.</summary>
public enum GyroButtonCombine
{
    Any,
    All
}

public enum TrackballFriction
{
    Off,
    Low,
    Medium,
    High,
    ExtraHigh
}

/// <summary>Steam-style haptic ticks while a trackpad is used as mouse / mouse-joystick.</summary>
public enum MouseHapticsIntensity
{
    Off,
    Low,
    Medium,
    High
}
