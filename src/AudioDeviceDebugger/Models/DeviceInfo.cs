namespace AudioDeviceDebugger.Models;

/// <summary>
/// Snapshot of a Core Audio endpoint. Deliberately a plain DTO (no NAudio/COM types) so it can be
/// bound directly from XAML and compared cheaply when diffing enumeration results on refresh.
/// </summary>
public sealed class DeviceInfo
{
    /// <summary>Core Audio endpoint ID, e.g. "{0.0.0.00000000}.{guid}". Stable across reboots for the same physical endpoint.</summary>
    public required string Id { get; init; }

    public required string FriendlyName { get; init; }

    public required AudioDirection Direction { get; init; }

    public required DeviceConnectionState State { get; init; }

    /// <summary>True if this endpoint is the default for Console + Multimedia roles (i.e. Windows' plain "Default Device").</summary>
    public bool IsDefaultDevice { get; init; }

    /// <summary>True if this endpoint is the default for the Communications role.</summary>
    public bool IsDefaultCommunicationsDevice { get; init; }

    /// <summary>PKEY_AudioEndpoint_FormFactor (EndpointFormFactor: 3 Headphones, 4 Microphone, 5 Headset,
    /// 6 Handset, …), or null if not reported.</summary>
    public int? FormFactor { get; init; }

    /// <summary>Headset or handset — a device whose mic and speaker share one (usually Bluetooth) link, so opening
    /// the mic switches it into call (hands-free) mode.</summary>
    public bool IsHeadsetFormFactor => FormFactor is 5 or 6;

    /// <summary>DEVPKEY_Device_ContainerId: the physical device this endpoint belongs to. All endpoints of one
    /// headset or dongle share it. Null if not reported.</summary>
    public Guid? ContainerId { get; init; }

    /// <summary>
    /// For an input with form factor 4 (Microphone): the name of an output endpoint in the same container — the
    /// mic and the speaker are one device, e.g. a headset dongle that reports its mic as a plain "Microphone"
    /// (Jabra Link 390, Jabra Evolve3 75). Null otherwise, and never set for the "this PC" container that holds all
    /// built-in endpoints.
    /// </summary>
    public string? LinkedOutputName { get; init; }

    /// <summary>A mic whose device also plays audio over the same link, so opening the mic can switch that device
    /// into call (hands-free) mode: a headset/handset form factor, or a plain mic with a linked output.</summary>
    public bool IsHeadsetMic =>
        Direction == AudioDirection.Input && (IsHeadsetFormFactor || LinkedOutputName is not null);

    public override string ToString() => FriendlyName;

    public override bool Equals(object? obj) =>
        obj is DeviceInfo other
        && Id == other.Id
        && Direction == other.Direction
        && State == other.State
        && IsDefaultDevice == other.IsDefaultDevice
        && IsDefaultCommunicationsDevice == other.IsDefaultCommunicationsDevice
        && FriendlyName == other.FriendlyName;

    public override int GetHashCode() => Id.GetHashCode();
}
