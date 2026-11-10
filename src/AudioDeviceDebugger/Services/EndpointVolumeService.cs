using AudioDeviceDebugger.Models;
using NAudio.CoreAudioApi;

namespace AudioDeviceDebugger.Services;

/// <summary>
/// Must be used from a single (UI) thread; only the <see cref="VolumeChanged"/> event arrives on another thread.
/// </summary>
public sealed class EndpointVolumeService : IEndpointVolumeService
{
    // Tagged onto every change we make, so the resulting notification can be told apart from external
    // changes and doesn't echo back into a slider the user is still dragging.
    private static readonly Guid OwnEventContext = Guid.NewGuid();

    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly Dictionary<string, Endpoint> _endpoints = new();
    private bool _disposed;

    public event EventHandler<EndpointVolumeChangedEventArgs>? VolumeChanged;

    public IReadOnlyCollection<string> MonitoredDeviceIds => _endpoints.Keys;

    public EndpointVolumeState? StartMonitoring(string deviceId)
    {
        if (_disposed) return null;

        if (_endpoints.TryGetValue(deviceId, out var existing))
        {
            return TryRead(existing.Volume);
        }

        MMDevice? device = null;
        try
        {
            device = _enumerator.GetDevice(deviceId);
            var volume = device.AudioEndpointVolume;
            volume.NotificationGuid = OwnEventContext;

            AudioEndpointVolumeNotificationDelegate handler = data =>
            {
                if (data.EventContext == OwnEventContext) return;
                VolumeChanged?.Invoke(this, new EndpointVolumeChangedEventArgs(
                    deviceId, new EndpointVolumeState(data.MasterVolume, data.Muted)));
            };
            volume.OnVolumeNotification += handler;

            _endpoints[deviceId] = new Endpoint(device, volume, handler);
            return TryRead(volume);
        }
        catch
        {
            // Not active / no endpoint volume interface — there's nothing to control.
            device?.Dispose();
            return null;
        }
    }

    public void StopMonitoring(string deviceId)
    {
        if (_endpoints.Remove(deviceId, out var endpoint))
        {
            Release(endpoint);
        }
    }

    public void SetVolume(string deviceId, float scalar) =>
        GetEndpoint(deviceId).Volume.MasterVolumeLevelScalar = Math.Clamp(scalar, 0f, 1f);

    public void SetMute(string deviceId, bool muted) =>
        GetEndpoint(deviceId).Volume.Mute = muted;

    private Endpoint GetEndpoint(string deviceId) =>
        _endpoints.TryGetValue(deviceId, out var endpoint)
            ? endpoint
            : throw new InvalidOperationException("Device has no volume control (not active).");

    private static EndpointVolumeState? TryRead(AudioEndpointVolume volume)
    {
        try
        {
            return new EndpointVolumeState(volume.MasterVolumeLevelScalar, volume.Mute);
        }
        catch
        {
            return null;
        }
    }

    private static void Release(Endpoint endpoint)
    {
        try
        {
            endpoint.Volume.OnVolumeNotification -= endpoint.Handler;
        }
        catch
        {
            // Best-effort.
        }
        endpoint.Device.Dispose(); // also disposes the AudioEndpointVolume (unregisters the COM callback)
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var endpoint in _endpoints.Values)
        {
            Release(endpoint);
        }
        _endpoints.Clear();
        _enumerator.Dispose();
    }

    private sealed record Endpoint(MMDevice Device, AudioEndpointVolume Volume, AudioEndpointVolumeNotificationDelegate Handler);
}
