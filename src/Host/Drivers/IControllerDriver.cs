using MistMapper.Shared;

namespace MistMapper.Host.Drivers;

public interface IControllerDriver : IDisposable
{
    string Id { get; }
    string DisplayName { get; }
    DriverCapabilities Capabilities { get; }
    bool IsConnected { get; }

    /// <summary>Stable identity for multi-pad slots (HID physical key or test id).</summary>
    string DeviceKey { get; }

    /// <summary>"sc1", "sc2", "dualsense", "dualsense-edge", or empty when unknown.</summary>
    string ControllerModel { get; }

    bool TryOpen();
    void Close();
    bool PrepareExclusive();
    bool RestoreExclusive();
    bool KeepAlive();
    bool TryRead(out InputFrame frame);

    /// <summary>
    /// Pulse motors so the user can identify which pad is which.
    /// Default: rumble briefly via <see cref="SetRumble"/>.
    /// </summary>
    async Task<bool> IdentifyAsync(CancellationToken ct = default)
    {
        SetRumble(0xC0, 0xC0);
        try { await Task.Delay(450, ct); }
        catch (OperationCanceledException) { /* restore */ }
        SetRumble(0, 0);
        return true;
    }

    /// <summary>
    /// Xbox-style motor speeds (0–255). Left = large/low-frequency, right = small/high-frequency.
    /// No-op on drivers that cannot rumble.
    /// </summary>
    void SetRumble(byte leftMotor, byte rightMotor) { }
}
