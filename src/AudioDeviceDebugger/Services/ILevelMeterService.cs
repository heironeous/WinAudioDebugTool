namespace AudioDeviceDebugger.Services;

public interface ILevelMeterService : IDisposable
{
    /// <summary>Starts metering the device via IAudioMeterInformation (no-op if already metered). For output
    /// devices this is a passive read. For input devices a shared-mode capture stream is also kept open (and its
    /// data discarded), because Windows only feeds a capture endpoint's peak meter while something is recording
    /// from it — the same thing the Sound control panel's Recording tab does to show its meters.
    /// <paramref name="interval"/> sets the polling period for all devices.</summary>
    void StartMonitoring(string deviceId, TimeSpan interval);

    void StopMonitoring(string deviceId);

    /// <summary>Raised on the service's background polling thread — subscribers must marshal to the UI thread.</summary>
    event EventHandler<DeviceLevelChangedEventArgs>? LevelChanged;
}
