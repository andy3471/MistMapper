using SteamControllerBridge.Host.Mapping;
using SteamControllerBridge.Host.Services;
using SteamControllerBridge.Host.Steam;
using SteamControllerBridge.Host.Viiper;
using SteamControllerBridge.Shared;

namespace SteamControllerBridge.Host.Services;

/// <summary>
/// Owns the HID → map → VIIPER loop, Steam pause, and lizard restore on lock.
/// </summary>
public sealed class BridgeService : IDisposable
{
    readonly ProfileService _profiles;
    readonly SteamWatcher _steam;
    readonly SessionWatcher _session;
    readonly MappingEngine _mapper = new();
    readonly object _gate = new();

    SteamControllerDevice? _controller;
    ViiperXbox360Client? _viiper;
    CancellationTokenSource? _cts;
    Task? _loop;
    BridgeStatus _status = new();
    DateTime _lastKeepalive = DateTime.MinValue;
    string? _lastError;

    public event Action<BridgeStatus>? StatusChanged;

    public BridgeService(ProfileService profiles, SteamWatcher steam, SessionWatcher session)
    {
        _profiles = profiles;
        _steam = steam;
        _session = session;
        _steam.Changed += _ => RefreshMode();
        _session.Changed += locked =>
        {
            if (locked) EnterLizardForLock();
            else RefreshMode();
        };
        _profiles.Changed += () => PublishStatus();
    }

    public BridgeStatus Status
    {
        get { lock (_gate) return CloneStatus(_status); }
    }

    public void Start()
    {
        if (_loop is not null) return;
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => RunLoopAsync(_cts.Token));
        PublishStatus(BridgeRunState.WaitingForController, "Starting…");
    }

    public void Stop()
    {
        _cts?.Cancel();
        try { _loop?.Wait(2000); } catch { /* ignore */ }
        TearDownBridge(restoreLizard: true);
        PublishStatus(BridgeRunState.Stopped, "Stopped");
    }

    public void SetEnabled(bool enabled)
    {
        _profiles.BridgeEnabled = enabled;
        RefreshMode();
    }

    void RefreshMode() => PublishStatus();

    async Task RunLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!_profiles.BridgeEnabled)
                {
                    TearDownBridge(restoreLizard: true);
                    PublishStatus(BridgeRunState.Stopped, "Bridge disabled");
                    await Task.Delay(500, ct);
                    continue;
                }

                if (_session.IsLocked)
                {
                    TearDownBridge(restoreLizard: true);
                    PublishStatus(BridgeRunState.PausedLocked, "Session locked — lizard mode");
                    await Task.Delay(400, ct);
                    continue;
                }

                if (_profiles.AutoPauseWhenSteamRunning && _steam.IsSteamRunning)
                {
                    TearDownBridge(restoreLizard: true);
                    PublishStatus(BridgeRunState.PausedSteam, "Steam running — bridge paused");
                    await Task.Delay(1000, ct);
                    continue;
                }

                if (_controller is null || !_controller.IsOpen)
                {
                    TearDownVirtualOnly();
                    _controller = new SteamControllerDevice();
                    if (!_controller.Open())
                    {
                        _controller.Dispose();
                        _controller = null;
                        PublishStatus(BridgeRunState.WaitingForController, "Waiting for Steam Controller…");
                        await Task.Delay(1000, ct);
                        continue;
                    }
                }

                // Enter bridge mode: disable lizard + ensure VIIPER
                if (_viiper is null || !_viiper.IsConnected)
                {
                    PublishStatus(BridgeRunState.WaitingForController, "Connecting to VIIPER…");
                    try
                    {
                        _viiper?.Dispose();
                        _viiper = new ViiperXbox360Client();
                        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        connectCts.CancelAfter(TimeSpan.FromSeconds(3));
                        await _viiper.ConnectAsync(connectCts.Token);
                    }
                    catch (Exception ex)
                    {
                        _lastError = ex.Message;
                        _viiper?.Dispose();
                        _viiper = null;
                        PublishStatus(BridgeRunState.Error,
                            "VIIPER unavailable. Start `viiper server` (usbip-win2 required). " + ex.Message);
                        await Task.Delay(2000, ct);
                        continue;
                    }

                    _controller.DisableLizardMode();
                    _lastKeepalive = DateTime.UtcNow;
                }

                // Read + map + send
                if (_controller.TryReadState(out var sc))
                {
                    var profile = _profiles.ActiveProfile;
                    var xbox = _mapper.Map(sc, profile);
                    _viiper.SendInput(xbox);

                    if (_mapper.TryConsumeMouseDelta(out int dx, out int dy))
                        MouseInjector.Move(dx, dy);

                    PublishStatus(BridgeRunState.Bridging, $"Bridging — {profile.Name}");
                }
                else if (!_controller.IsOpen)
                {
                    TearDownBridge(restoreLizard: false);
                    PublishStatus(BridgeRunState.WaitingForController, "Controller disconnected");
                }

                if ((DateTime.UtcNow - _lastKeepalive).TotalMilliseconds > 800 && _controller.IsOpen)
                {
                    _controller.SendKeepalive();
                    _lastKeepalive = DateTime.UtcNow;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                PublishStatus(BridgeRunState.Error, ex.Message);
                await Task.Delay(1000, ct);
            }
        }

        TearDownBridge(restoreLizard: true);
    }

    void EnterLizardForLock()
    {
        try
        {
            if (_controller is { IsOpen: true })
                _controller.EnableLizardMode();
        }
        catch { /* ignore */ }
        TearDownVirtualOnly();
        PublishStatus(BridgeRunState.PausedLocked, "Session locked — lizard mode");
    }

    void TearDownVirtualOnly()
    {
        try { _viiper?.Dispose(); } catch { }
        _viiper = null;
    }

    void TearDownBridge(bool restoreLizard)
    {
        TearDownVirtualOnly();
        if (_controller is not null)
        {
            try
            {
                if (restoreLizard && _controller.IsOpen)
                    _controller.EnableLizardMode();
            }
            catch { /* ignore */ }
            _controller.Dispose();
            _controller = null;
        }
    }

    void PublishStatus(BridgeRunState? state = null, string? message = null)
    {
        BridgeStatus snap;
        lock (_gate)
        {
            if (state.HasValue) _status.State = state.Value;
            if (message is not null) _status.Message = message;
            _status.BridgeEnabled = _profiles.BridgeEnabled;
            _status.ControllerConnected = _controller?.IsOpen == true;
            _status.SteamRunning = _steam.IsSteamRunning;
            _status.SessionLocked = _session.IsLocked;
            _status.ViiperConnected = _viiper?.IsConnected == true;
            var active = _profiles.ActiveProfile;
            _status.ActiveProfileId = active.Id;
            _status.ActiveProfileName = active.Name;
            _status.UpdatedAt = DateTimeOffset.UtcNow;
            if (!string.IsNullOrEmpty(_lastError) && _status.State == BridgeRunState.Error)
                _status.Message = string.IsNullOrEmpty(message) ? _lastError : message;
            snap = CloneStatus(_status);
        }
        StatusChanged?.Invoke(snap);
    }

    static BridgeStatus CloneStatus(BridgeStatus s) => new()
    {
        State = s.State,
        BridgeEnabled = s.BridgeEnabled,
        ControllerConnected = s.ControllerConnected,
        SteamRunning = s.SteamRunning,
        SessionLocked = s.SessionLocked,
        ViiperConnected = s.ViiperConnected,
        ActiveProfileId = s.ActiveProfileId,
        ActiveProfileName = s.ActiveProfileName,
        Message = s.Message,
        UpdatedAt = s.UpdatedAt
    };

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }
}
