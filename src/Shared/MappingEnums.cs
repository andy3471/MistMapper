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
    ButtonPad
}

public enum GyroMode
{
    Off,
    AsRightStick,
    AsMouse
}
