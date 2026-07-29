using MistMapper.Host.Drivers;
using MistMapper.Shared;

namespace MistMapper.Tests.Fakes;

public sealed class FakeControllerDriver : IControllerDriver
{
    readonly Queue<InputFrame> _frames = new();
    bool _open;

    public string Id { get; init; } = "fake-controller";
    public string DisplayName { get; init; } = "Fake Controller";
    public string DeviceKey { get; init; } = "fake-controller";
    public string ControllerModel { get; init; } = "sc2";
    public DriverCapabilities Capabilities { get; init; } = new()
    {
        DriverId = "fake-controller",
        DisplayName = "Fake Controller"
    };

    public bool IsConnected => _open;
    public List<(byte Left, byte Right)> RumbleHistory { get; } = [];

    public void Enqueue(InputFrame frame) => _frames.Enqueue(frame);

    public bool TryOpen()
    {
        _open = true;
        return true;
    }

    public void Close() => _open = false;

    public bool PrepareExclusive() => true;
    public bool RestoreExclusive() => true;
    public bool KeepAlive() => true;

    public void SetRumble(byte leftMotor, byte rightMotor) =>
        RumbleHistory.Add((leftMotor, rightMotor));

    public bool TryRead(out InputFrame frame)
    {
        if (_frames.Count > 0)
        {
            frame = _frames.Dequeue();
            return true;
        }

        frame = new InputFrame();
        return false;
    }

    public void Dispose() => Close();
}
