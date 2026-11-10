namespace AudioDeviceDebugger.Services;

public interface ILevelMeterService : IDisposable
{
    /// <summary>Starts polling the device's IAudioMeterInformation on a timer. Safe to call for every
    /// device in the list at once — this reads a passive meter, it does not open an exclusive stream,
    /// so it never conflicts with another app (or this tool's own test playback/recording) using the
    /// same device.</summary>
    void StartMonitoring(string deviceId, TimeSpan interval);

    void StopMonitoring(string deviceId);

    /// <summary>Raised on a background timer thread — subscribers must marshal to the UI thread.</summary>
    event EventHandler<DeviceLevelChangedEventArgs>? LevelChanged;
}
