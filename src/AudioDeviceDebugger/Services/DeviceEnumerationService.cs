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

    public event EventHandler<EndpointNotification>? EndpointNotified;

    public DeviceEnumerationService()
    {
        _notificationClient = new NotificationClient(RaiseDevicesChanged, RaiseEndpointNotified);
        _enumerator.RegisterEndpointNotificationCallback(_notificationClient);
    }

    public string? TryGetFriendlyName(string deviceId) =>
        _propertyReader.TryGetString(deviceId, PropertyKeys.PKEY_Device_FriendlyName);

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
                    FormFactor = (int?)_propertyReader.TryGetUInt32(device.ID, PropertyKeys.PKEY_AudioEndpoint_FormFactor),
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

    internal static DeviceConnectionState MapState(DeviceState state) => state switch
    {
        DeviceState.Active => DeviceConnectionState.Active,
        DeviceState.Disabled => DeviceConnectionState.Disabled,
        DeviceState.NotPresent => DeviceConnectionState.NotPresent,
        DeviceState.Unplugged => DeviceConnectionState.Unplugged,
        _ => DeviceConnectionState.NotPresent,
    };

    private void RaiseDevicesChanged() => DevicesChanged?.Invoke(this, EventArgs.Empty);

    private void RaiseEndpointNotified(EndpointNotification notification) => EndpointNotified?.Invoke(this, notification);

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
    /// Forwards Core Audio endpoint notifications: each one is reported in detail (<see cref="EndpointNotified"/>,
    /// for the event timeline), and all but property changes also trigger a re-enumeration. Property changes don't —
    /// they fire constantly on some endpoints and would cause a refresh storm. Callbacks arrive on system threads and
    /// must return quickly, so nothing here touches COM: the timestamp is taken now, everything else is resolved later
    /// on the UI thread.
    /// </summary>
    private sealed class NotificationClient : IMMNotificationClient
    {
        private readonly Action _onChanged;
        private readonly Action<EndpointNotification> _onNotification;

        public NotificationClient(Action onChanged, Action<EndpointNotification> onNotification)
        {
            _onChanged = onChanged;
            _onNotification = onNotification;
        }

        public void OnDeviceStateChanged(string deviceId, DeviceState newState)
        {
            Notify(new EndpointNotification(EndpointNotificationKind.StateChanged, deviceId) { NewState = MapState(newState) });
            _onChanged();
        }

        public void OnDeviceAdded(string pwstrDeviceId)
        {
            Notify(new EndpointNotification(EndpointNotificationKind.Added, pwstrDeviceId));
            _onChanged();
        }

        public void OnDeviceRemoved(string deviceId)
        {
            Notify(new EndpointNotification(EndpointNotificationKind.Removed, deviceId));
            _onChanged();
        }

        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            Notify(new EndpointNotification(EndpointNotificationKind.DefaultChanged,
                string.IsNullOrEmpty(defaultDeviceId) ? null : defaultDeviceId)
            {
                Flow = flow == DataFlow.Capture ? AudioDirection.Input : AudioDirection.Output,
                Role = role switch
                {
                    Role.Console => DefaultRole.Console,
                    Role.Multimedia => DefaultRole.Multimedia,
                    _ => DefaultRole.Communications,
                },
            });
            _onChanged();
        }

        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) =>
            Notify(new EndpointNotification(EndpointNotificationKind.PropertyChanged, pwstrDeviceId)
            {
                PropertyFormatId = key.formatId,
                PropertyId = key.propertyId,
            });

        private void Notify(EndpointNotification notification)
        {
            try
            {
                _onNotification(notification);
            }
            catch
            {
                // Never let a logging problem propagate into the audio service's callback.
            }
        }
    }
}

/// <summary>What an IMMNotificationClient callback reported.</summary>
public enum EndpointNotificationKind
{
    Added,
    Removed,
    StateChanged,
    DefaultChanged,
    PropertyChanged,
}

/// <summary>The three default-device roles Windows tracks per direction.</summary>
public enum DefaultRole
{
    Console,
    Multimedia,
    Communications,
}

/// <summary>One endpoint notification, timestamped when Windows delivered it.</summary>
public sealed record EndpointNotification(EndpointNotificationKind Kind, string? DeviceId)
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;
    public DeviceConnectionState? NewState { get; init; }
    public AudioDirection? Flow { get; init; }
    public DefaultRole? Role { get; init; }
    public Guid? PropertyFormatId { get; init; }
    public int? PropertyId { get; init; }
}
