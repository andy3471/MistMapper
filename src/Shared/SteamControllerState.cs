namespace MistMapper.Shared;

/// <summary>Decoded Steam Controller 2026 state from a 0x42/0x45 report.</summary>
public sealed class SteamControllerState
{
    public byte Sequence { get; set; }

    public bool A { get; set; }
    public bool B { get; set; }
    public bool X { get; set; }
    public bool Y { get; set; }
    public bool Lb { get; set; }
    public bool Rb { get; set; }
    public bool View { get; set; }
    public bool Menu { get; set; }
    public bool Steam { get; set; }
    public bool LsClick { get; set; }
    public bool RsClick { get; set; }
    public bool DpadUp { get; set; }
    public bool DpadDown { get; set; }
    public bool DpadLeft { get; set; }
    public bool DpadRight { get; set; }
    public bool L4 { get; set; }
    public bool L5 { get; set; }
    public bool R4 { get; set; }
    public bool R5 { get; set; }
    public bool LeftTrackpadTouch { get; set; }
    public bool RightTrackpadTouch { get; set; }
    public bool LeftTrackpadClick { get; set; }
    public bool RightTrackpadClick { get; set; }

    public ushort LeftTrigger { get; set; }
    public ushort RightTrigger { get; set; }
    public short LeftStickX { get; set; }
    public short LeftStickY { get; set; }
    public short RightStickX { get; set; }
    public short RightStickY { get; set; }
    public short LeftTrackpadX { get; set; }
    public short LeftTrackpadY { get; set; }
    public short RightTrackpadX { get; set; }
    public short RightTrackpadY { get; set; }

    public short GyroX { get; set; }
    public short GyroY { get; set; }
    public short GyroZ { get; set; }
    public bool HasImu { get; set; }
}
