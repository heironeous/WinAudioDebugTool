namespace AudioDeviceDebugger.Services;

public interface ILevelMeterService : IDisposable
{
    /// <summary>Starts polling the device's peak meter (IAudioMeterInformation; no-op if already metered). This is a
    /// passive read and never opens a stream. Note that Windows only feeds an <em>input</em> endpoint's meter while
    /// something is recording from it — see <see cref="SetCaptureStreamDevice"/>.
    /// <paramref name="interval"/> sets the polling period for all devices.</summary>
    void StartMonitoring(string deviceId, TimeSpan interval);

    void StopMonitoring(string deviceId);

    /// <summary>Keeps a silent shared-mode capture stream open on this one input device (data discarded) so its meter
    /// shows live levels — the same trick the Sound control panel's Recording tab uses. Pass null (or an output
    /// device) to close it. Any previously chosen device's stream is closed: opening a headset mic switches it into
    /// call / hands-free mode, so at most one mic is held open, and only while the user is looking at it.</summary>
    void SetCaptureStreamDevice(string? deviceId);

    /// <summary>Raised on the service's background polling thread — subscribers must marshal to the UI thread.</summary>
    event EventHandler<DeviceLevelChangedEventArgs>? LevelChanged;
}
