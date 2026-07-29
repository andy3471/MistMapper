using MistMapper.Host.Steam;
using MistMapper.Shared;

namespace MistMapper.Host.Drivers;

public sealed class SteamControllerDriver : IControllerDriver
{
    SteamControllerDevice? _device;

    public string Id => DriverIds.SteamController;
    public string DisplayName => "Steam Controller";
    public DriverCapabilities Capabilities { get; } = SteamControllerCapabilities.Create();
    public bool IsConnected => _device?.IsOpen == true;
    public int ProductId => _device?.ProductId ?? 0;
    public string ControllerModel =>
        _device is null ? "" : SteamControllerDevice.ClassifyModel(_device.ProductId);

    public bool TryOpen()
    {
        Close();
        var device = new SteamControllerDevice();
        if (!device.Open())
        {
            device.Dispose();
            return false;
        }
        _device = device;
        return true;
    }

    public void Close()
    {
        _device?.Dispose();
        _device = null;
    }

    public bool PrepareExclusive() => _device?.DisableLizardMode() == true;
    public bool RestoreExclusive() => _device?.EnableLizardMode() == true;
    public bool KeepAlive() => _device?.SendKeepalive() == true;

    public bool TryRead(out InputFrame frame)
    {
        frame = new InputFrame();
        if (_device is null) return false;
        if (!_device.TryReadState(out var sc))
        {
            if (!_device.IsOpen)
            {
                Close();
                return false;
            }
            return false;
        }

        frame = ToFrame(sc);
        return true;
    }

    public static InputFrame ToFrame(SteamControllerState sc)
    {
        var frame = new InputFrame { Timestamp = DateTimeOffset.UtcNow };
        void Dig(PhysicalInput id, bool pressed) => frame.Digitals[id.ToString()] = pressed;

        Dig(PhysicalInput.A, sc.A);
        Dig(PhysicalInput.B, sc.B);
        Dig(PhysicalInput.X, sc.X);
        Dig(PhysicalInput.Y, sc.Y);
        Dig(PhysicalInput.Lb, sc.Lb);
        Dig(PhysicalInput.Rb, sc.Rb);
        Dig(PhysicalInput.View, sc.View);
        Dig(PhysicalInput.Menu, sc.Menu);
        Dig(PhysicalInput.Steam, sc.Steam);
        Dig(PhysicalInput.LsClick, sc.LsClick);
        Dig(PhysicalInput.RsClick, sc.RsClick);
        Dig(PhysicalInput.DpadUp, sc.DpadUp);
        Dig(PhysicalInput.DpadDown, sc.DpadDown);
        Dig(PhysicalInput.DpadLeft, sc.DpadLeft);
        Dig(PhysicalInput.DpadRight, sc.DpadRight);
        Dig(PhysicalInput.L4, sc.L4);
        Dig(PhysicalInput.L5, sc.L5);
        Dig(PhysicalInput.R4, sc.R4);
        Dig(PhysicalInput.R5, sc.R5);
        Dig(PhysicalInput.LeftTrackpadClick, sc.LeftTrackpadClick);
        Dig(PhysicalInput.RightTrackpadClick, sc.RightTrackpadClick);
        Dig(PhysicalInput.LeftTrackpad, sc.LeftTrackpadTouch);
        Dig(PhysicalInput.RightTrackpad, sc.RightTrackpadTouch);

        frame.Analogs[PhysicalInput.Lt.ToString()] = Math.Clamp(sc.LeftTrigger / 32767f, 0f, 1f);
        frame.Analogs[PhysicalInput.Rt.ToString()] = Math.Clamp(sc.RightTrigger / 32767f, 0f, 1f);
        frame.Vectors[PhysicalInput.LeftStick.ToString()] = (sc.LeftStickX / 32767f, sc.LeftStickY / 32767f);
        frame.Vectors[PhysicalInput.RightStick.ToString()] = (sc.RightStickX / 32767f, sc.RightStickY / 32767f);
        frame.Vectors[PhysicalInput.LeftTrackpad.ToString()] = (sc.LeftTrackpadX / 32767f, sc.LeftTrackpadY / 32767f);
        frame.Vectors[PhysicalInput.RightTrackpad.ToString()] = (sc.RightTrackpadX / 32767f, sc.RightTrackpadY / 32767f);
        if (sc.HasImu)
            frame.Vectors[PhysicalInput.Gyro.ToString()] = (sc.GyroX / 32767f, sc.GyroY / 32767f);

        return frame;
    }

    public void Dispose() => Close();
}
