using AudioDeviceDebugger.Models;

namespace AudioDeviceDebugger.Services;

public sealed class DeviceLevelChangedEventArgs : EventArgs
{
    public DeviceLevelChangedEventArgs(string deviceId, LevelSample sample)
    {
        DeviceId = deviceId;
        Sample = sample;
    }

    public string DeviceId { get; }
    public LevelSample Sample { get; }
}
