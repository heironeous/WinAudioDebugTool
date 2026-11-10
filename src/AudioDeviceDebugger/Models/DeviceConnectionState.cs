namespace AudioDeviceDebugger.Models;

/// <summary>Mirrors NAudio's DeviceState / Windows' DEVICE_STATE_xxx flags.</summary>
public enum DeviceConnectionState
{
    Active,
    Disabled,
    NotPresent,
    Unplugged,
}
