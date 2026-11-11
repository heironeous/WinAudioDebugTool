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
