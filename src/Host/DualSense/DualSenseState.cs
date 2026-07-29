namespace MistMapper.Host.DualSense;

/// <summary>Decoded DualSense / DualSense Edge state from a full USB or BT report.</summary>
public sealed class DualSenseState
{
    public byte Sequence { get; set; }

    public bool Cross { get; set; }
    public bool Circle { get; set; }
    public bool Square { get; set; }
    public bool Triangle { get; set; }
    public bool L1 { get; set; }
    public bool R1 { get; set; }
    public bool Create { get; set; }
    public bool Options { get; set; }
    public bool Ps { get; set; }
    public bool Mute { get; set; }
    public bool TouchpadClick { get; set; }
    public bool L3 { get; set; }
    public bool R3 { get; set; }
    public bool DpadUp { get; set; }
    public bool DpadDown { get; set; }
    public bool DpadLeft { get; set; }
    public bool DpadRight { get; set; }
    public bool LeftPaddle { get; set; }
    public bool RightPaddle { get; set; }

    public byte LeftTrigger { get; set; }
    public byte RightTrigger { get; set; }
    public byte LeftStickX { get; set; }
    public byte LeftStickY { get; set; }
    public byte RightStickX { get; set; }
    public byte RightStickY { get; set; }

    public bool TouchpadTouch { get; set; }
    public short TouchpadX { get; set; }
    public short TouchpadY { get; set; }

    public short GyroX { get; set; }
    public short GyroY { get; set; }
    public short GyroZ { get; set; }
    public bool HasImu { get; set; }
}
