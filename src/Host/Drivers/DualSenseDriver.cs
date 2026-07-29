using MistMapper.Host.DualSense;
using MistMapper.Shared;

namespace MistMapper.Host.Drivers;

public sealed class DualSenseDriver : IControllerDriver
{
    DualSenseDevice? _device;
    string _deviceKey = "";
    string _model = "";

    public string Id => DriverIds.DualSense;
    public string DisplayName { get; private set; } = "DualSense";
    public DriverCapabilities Capabilities { get; private set; } = DualSenseCapabilities.Create();
    public bool IsConnected => _device?.IsOpen == true;
    public string DeviceKey => _deviceKey;
    public int ProductId => _device?.ProductId ?? 0;
    public string ControllerModel => _model;

    public bool TryOpen()
    {
        Close();
        var device = new DualSenseDevice();
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
        var device = new DualSenseDevice();
        if (!device.Open(devicePathOrKey))
        {
            device.Dispose();
            return false;
        }
        Attach(device);
        return true;
    }

    public static DualSenseDriver? TryOpenPath(string devicePathOrKey)
    {
        var driver = new DualSenseDriver();
        if (!driver.TryOpen(devicePathOrKey))
        {
            driver.Dispose();
            return null;
        }
        return driver;
    }

    void Attach(DualSenseDevice device)
    {
        _device = device;
        var path = device.DevicePath ?? "";
        _deviceKey = string.IsNullOrEmpty(path)
            ? Guid.NewGuid().ToString("N")
            : DualSenseDevice.PhysicalDeviceKey(path);
        _model = device.Model;
        DisplayName = DualSenseDevice.DisplayNameForModel(_model);
        Capabilities = DualSenseCapabilities.Create(_model);
    }

    public void Close()
    {
        _device?.Dispose();
        _device = null;
        _deviceKey = "";
        _model = "";
        DisplayName = "DualSense";
        Capabilities = DualSenseCapabilities.Create();
    }

    public bool PrepareExclusive() => _device?.HideNativeGamepad() == true;
    public bool RestoreExclusive()
    {
        _device?.RestoreNativeGamepad();
        return true;
    }
    public bool KeepAlive() => true;

    public Task<bool> IdentifyAsync(CancellationToken ct = default) =>
        _device?.IdentifyAsync(ct) ?? Task.FromResult(false);

    public void SetRumble(byte leftMotor, byte rightMotor) =>
        _device?.SetRumble(leftMotor, rightMotor);

    public bool TryRead(out InputFrame frame)
    {
        frame = new InputFrame();
        if (_device is null) return false;
        if (!_device.TryReadState(out var ds))
        {
            if (!_device.IsOpen)
            {
                Close();
                return false;
            }
            return false;
        }

        frame = ToFrame(ds);
        return true;
    }

    public static InputFrame ToFrame(DualSenseState ds)
    {
        var frame = new InputFrame { Timestamp = DateTimeOffset.UtcNow };
        void Dig(PhysicalInput id, bool pressed) => frame.Digitals[id.ToString()] = pressed;

        Dig(PhysicalInput.A, ds.Cross);
        Dig(PhysicalInput.B, ds.Circle);
        Dig(PhysicalInput.X, ds.Square);
        Dig(PhysicalInput.Y, ds.Triangle);
        Dig(PhysicalInput.Lb, ds.L1);
        Dig(PhysicalInput.Rb, ds.R1);
        Dig(PhysicalInput.View, ds.Create);
        Dig(PhysicalInput.Menu, ds.Options);
        Dig(PhysicalInput.Steam, ds.Ps);
        Dig(PhysicalInput.LsClick, ds.L3);
        Dig(PhysicalInput.RsClick, ds.R3);
        Dig(PhysicalInput.DpadUp, ds.DpadUp);
        Dig(PhysicalInput.DpadDown, ds.DpadDown);
        Dig(PhysicalInput.DpadLeft, ds.DpadLeft);
        Dig(PhysicalInput.DpadRight, ds.DpadRight);
        Dig(PhysicalInput.L4, ds.LeftPaddle);
        Dig(PhysicalInput.R4, ds.RightPaddle);
        Dig(PhysicalInput.RightTrackpadClick, ds.TouchpadClick);
        Dig(PhysicalInput.RightTrackpad, ds.TouchpadTouch);
        // No capacitive stick touch — mirror touchpad so FPS layouts that activate
        // gyro on RightStickTouch (Steam default) still work on DualSense.
        Dig(PhysicalInput.RightStickTouch, ds.TouchpadTouch);

        frame.Analogs[PhysicalInput.Lt.ToString()] = ds.LeftTrigger / 255f;
        frame.Analogs[PhysicalInput.Rt.ToString()] = ds.RightTrigger / 255f;
        frame.Vectors[PhysicalInput.LeftStick.ToString()] = Stick(ds.LeftStickX, ds.LeftStickY);
        frame.Vectors[PhysicalInput.RightStick.ToString()] = Stick(ds.RightStickX, ds.RightStickY);
        frame.Vectors[PhysicalInput.RightTrackpad.ToString()] = (ds.TouchpadX / 32767f, ds.TouchpadY / 32767f);
        if (ds.HasImu)
            frame.Vectors[PhysicalInput.Gyro.ToString()] = (ds.GyroX / 32767f, ds.GyroY / 32767f);

        return frame;
    }

    static (float X, float Y) Stick(byte x, byte y)
    {
        // SDL: ((v * 257) - 32768) / 32767
        float nx = ((x * 257) - 32768) / 32767f;
        float ny = ((y * 257) - 32768) / 32767f;
        return (Math.Clamp(nx, -1f, 1f), Math.Clamp(ny, -1f, 1f));
    }

    public void Dispose() => Close();
}
