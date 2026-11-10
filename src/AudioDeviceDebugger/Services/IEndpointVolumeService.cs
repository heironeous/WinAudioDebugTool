using AudioDeviceDebugger.Models;

namespace AudioDeviceDebugger.Services;

/// <summary>Reads and changes each endpoint's master volume / mute via IAudioEndpointVolume — the same
/// setting Windows' volume flyout and Sound settings control (for inputs this is the mic level).</summary>
public interface IEndpointVolumeService : IDisposable
{
    /// <summary>Raised when an endpoint's volume or mute is changed by something other than this service
    /// (Windows volume flyout, keyboard media keys, another app). Raised on a COM worker thread.</summary>
    event EventHandler<EndpointVolumeChangedEventArgs>? VolumeChanged;

    /// <summary>Device IDs currently being watched.</summary>
    IReadOnlyCollection<string> MonitoredDeviceIds { get; }

    /// <summary>Starts watching the device for external volume changes (no-op if already watching) and
    /// returns its current state, or null if the endpoint has no volume control right now (not active).</summary>
    EndpointVolumeState? StartMonitoring(string deviceId);

    void StopMonitoring(string deviceId);

    /// <summary>Sets the master volume as a 0.0–1.0 scalar. The device must be monitored.</summary>
    void SetVolume(string deviceId, float scalar);

    /// <summary>Mutes / unmutes the endpoint. The device must be monitored.</summary>
    void SetMute(string deviceId, bool muted);
}

public sealed class EndpointVolumeChangedEventArgs : EventArgs
{
    public EndpointVolumeChangedEventArgs(string deviceId, EndpointVolumeState state)
    {
        DeviceId = deviceId;
        State = state;
    }

    public string DeviceId { get; }
    public EndpointVolumeState State { get; }
}
