using MistMapper.Host.Steam;
using MistMapper.Shared;

namespace MistMapper.Host.Drivers;

public sealed class SteamControllerDriver : IControllerDriver
{
    SteamControllerDevice? _device;
    string _deviceKey = "";
    string _model = "";

    public string Id => DriverIds.SteamController;
    public string DisplayName { get; private set; } = "Steam Controller";
    public DriverCapabilities Capabilities { get; private set; } = SteamControllerCapabilities.Create();
    public bool IsConnected => _device?.IsOpen == true;
    public string DeviceKey => _deviceKey;
    public int ProductId => _device?.ProductId ?? 0;
    public string ControllerModel => _model;

    public bool TryOpen()
    {
        Close();
        var device = new SteamControllerDevice();
        if (!device.Open())
        {
            device.Dispose();
            return false;
        }
        Attach(device);
        return true;
    }

    public bool TryOpen(string devicePathOrKey)
    {
        Close();
        var device = new SteamControllerDevice();
        if (!device.Open(devicePathOrKey))
        {
            device.Dispose();
            return false;
        }
        Attach(device);
        return true;
    }

    public static SteamControllerDriver? TryOpenPath(string devicePathOrKey)
    {
        var driver = new SteamControllerDriver();
        if (!driver.TryOpen(devicePathOrKey))
        {
            driver.Dispose();
            return null;
        }
        return driver;
    }

    void Attach(SteamControllerDevice device)
    {
        _device = device;
        var path = device.DevicePath ?? "";
        _deviceKey = string.IsNullOrEmpty(path)
            ? Guid.NewGuid().ToString("N")
            : SteamControllerDevice.PhysicalDeviceKey(path);
        _model = device.Model;
        DisplayName = SteamControllerDevice.DisplayNameForModel(_model);
        Capabilities = SteamControllerCapabilities.Create(_model);
    }

    public void Close()
    {
        _device?.Dispose();
        _device = null;
        _deviceKey = "";
        _model = "";
        DisplayName = "Steam Controller";
        Capabilities = SteamControllerCapabilities.Create();
    }

    public bool PrepareExclusive() => _device?.DisableLizardMode() == true;
    public bool RestoreExclusive() => _device?.EnableLizardMode() == true;
    public bool KeepAlive() => _device?.SendKeepalive() == true;

    public Task<bool> IdentifyAsync(CancellationToken ct = default) =>
        _device?.IdentifyAsync(ct) ?? Task.FromResult(false);

    public void SetRumble(byte leftMotor, byte rightMotor) =>
        _device?.SetRumble(leftMotor, rightMotor);

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

        frame = ToFrame(sc, isSc1: string.Equals(_model, "sc1", StringComparison.OrdinalIgnoreCase));
        return true;
    }

    public static InputFrame ToFrame(SteamControllerState sc, bool isSc1 = false)
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
        Dig(PhysicalInput.LeftStickTouch, sc.LeftStickTouch);
        // SC1 has no capacitive stick touch — mirror right-pad touch so layouts that
        // activate gyro on RightStickTouch (Steam FPS default) still work.
        Dig(PhysicalInput.RightStickTouch,
            sc.RightStickTouch || (isSc1 && sc.RightTrackpadTouch));

        frame.Analogs[PhysicalInput.Lt.ToString()] = Math.Clamp(sc.LeftTrigger / 32767f, 0f, 1f);
        frame.Analogs[PhysicalInput.Rt.ToString()] = Math.Clamp(sc.RightTrigger / 32767f, 0f, 1f);
        frame.Vectors[PhysicalInput.LeftStick.ToString()] = (sc.LeftStickX / 32767f, sc.LeftStickY / 32767f);
        frame.Vectors[PhysicalInput.RightStick.ToString()] = (sc.RightStickX / 32767f, sc.RightStickY / 32767f);
        frame.Vectors[PhysicalInput.LeftTrackpad.ToString()] = (sc.LeftTrackpadX / 32767f, sc.LeftTrackpadY / 32767f);
        frame.Vectors[PhysicalInput.RightTrackpad.ToString()] = (sc.RightTrackpadX / 32767f, sc.RightTrackpadY / 32767f);
        if (sc.HasImu)
        {
            // SC1 pitch is opposite the SC2 / MappingEngine convention (vertical look).
            float gx = sc.GyroX / 32767f;
            float gy = sc.GyroY / 32767f;
            if (isSc1)
                gx = -gx;
            frame.Vectors[PhysicalInput.Gyro.ToString()] = (gx, gy);
        }

        return frame;
    }

    public void Dispose() => Close();
}
