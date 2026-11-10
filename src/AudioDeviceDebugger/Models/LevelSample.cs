namespace AudioDeviceDebugger.Models;

/// <summary>One poll of a device's IAudioMeterInformation. Values are 0.0–1.0 peak, already normalized
/// by Windows — no dB math needed on our end.</summary>
public sealed class LevelSample
{
    public required DateTime Timestamp { get; init; }
    public required float MasterPeak { get; init; }
    public required IReadOnlyList<float> ChannelPeaks { get; init; }
}
