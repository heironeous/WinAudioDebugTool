namespace AudioDeviceDebugger.Models;

/// <summary>Deep-dive info for a single device, fetched on demand (unlike DeviceInfo, which is cheap to
/// produce for every device during enumeration).</summary>
public sealed class DeviceDetails
{
    public required string Id { get; init; }
    public required string FriendlyName { get; init; }
    public required AudioDirection Direction { get; init; }
    public required DeviceConnectionState State { get; init; }

    /// <summary>The shared-mode mix format WASAPI reports (what every app sharing the device gets resampled to).</summary>
    public AudioFormatInfo? SharedModeFormat { get; init; }

    /// <summary>The format exclusive-mode playback/capture would actually use, if it differs from shared mode.</summary>
    public AudioFormatInfo? ExclusiveModeFormat { get; init; }

    /// <summary>Inputs: whether the driver says it supports raw (unprocessed) capture — the endpoint property
    /// System.Devices.AudioDevice.RawProcessingSupported; null if it isn't set.</summary>
    public bool? RawProcessingSupported { get; init; }
}
