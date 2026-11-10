using AudioDeviceDebugger.Interop;
using AudioDeviceDebugger.Models;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace AudioDeviceDebugger.Services;

public sealed class DeviceEnumerationService : IDeviceEnumerationService
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly EndpointPropertyReader _propertyReader = new();
    private readonly NotificationClient _notificationClient;
    private bool _disposed;

    public event EventHandler? DevicesChanged;

    public DeviceEnumerationService()
    {
        _notificationClient = new NotificationClient(RaiseDevicesChanged);
        _enumerator.RegisterEndpointNotificationCallback(_notificationClient);
    }

    public IReadOnlyList<DeviceInfo> GetDevices()
    {
        var result = new List<DeviceInfo>();
        AddForDirection(result, DataFlow.Render, AudioDirection.Output);
        AddForDirection(result, DataFlow.Capture, AudioDirection.Input);
        return result;
    }

    private void AddForDirection(List<DeviceInfo> result, DataFlow dataFlow, AudioDirection direction)
    {
        string? defaultId = TryGetDefaultEndpointId(dataFlow, Role.Multimedia);
        string? defaultCommsId = TryGetDefaultEndpointId(dataFlow, Role.Communications);

        var collection = _enumerator.EnumerateAudioEndPoints(dataFlow, DeviceState.All);
        foreach (var device in collection)
        {
            using (device)
            {
                result.Add(new DeviceInfo
                {
                    Id = device.ID,
                    FriendlyName = SafeFriendlyName(device),
                    Direction = direction,
                    State = MapState(device.State),
                    IsDefaultDevice = string.Equals(device.ID, defaultId, StringComparison.Ordinal),
                    IsDefaultCommunicationsDevice = string.Equals(device.ID, defaultCommsId, StringComparison.Ordinal),
                });
            }
        }
    }

    private string? TryGetDefaultEndpointId(DataFlow dataFlow, Role role)
    {
        try
        {
            using var device = _enumerator.GetDefaultAudioEndpoint(dataFlow, role);
            return device.ID;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // No default endpoint registered for this flow/role (e.g. no capture devices at all).
            return null;
        }
    }

    // Not NAudio's device.FriendlyName — see EndpointPropertyReader for why that's ~450 ms per device.
    private string SafeFriendlyName(MMDevice device) =>
        _propertyReader.TryGetString(device.ID, PropertyKeys.PKEY_Device_FriendlyName) ?? device.ID;

    private static DeviceConnectionState MapState(DeviceState state) => state switch
    {
        DeviceState.Active => DeviceConnectionState.Active,
        DeviceState.Disabled => DeviceConnectionState.Disabled,
        DeviceState.NotPresent => DeviceConnectionState.NotPresent,
        DeviceState.Unplugged => DeviceConnectionState.Unplugged,
        _ => DeviceConnectionState.NotPresent,
    };

    private void RaiseDevicesChanged() => DevicesChanged?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            _enumerator.UnregisterEndpointNotificationCallback(_notificationClient);
        }
        catch
        {
            // Best-effort; enumerator is being torn down regardless.
        }
        _enumerator.Dispose();
        _propertyReader.Dispose();
    }

    /// <summary>
    /// Forwards Core Audio endpoint notifications to a single "something changed, re-enumerate" callback.
    /// Deliberately ignores per-property noise (OnPropertyValueChanged fires constantly, e.g. for meter
    /// updates) so we don't trigger a full refresh storm.
    /// </summary>
    private sealed class NotificationClient : IMMNotificationClient
    {
        private readonly Action _onChanged;

        public NotificationClient(Action onChanged) => _onChanged = onChanged;

        public void OnDeviceStateChanged(string deviceId, DeviceState newState) => _onChanged();

        public void OnDeviceAdded(string pwstrDeviceId) => _onChanged();

        public void OnDeviceRemoved(string deviceId) => _onChanged();

        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId) => _onChanged();

        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key)
        {
            // Intentionally ignored.
        }
    }
}
