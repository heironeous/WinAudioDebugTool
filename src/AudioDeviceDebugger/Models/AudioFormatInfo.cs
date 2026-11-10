namespace AudioDeviceDebugger.Models;

public sealed class AudioFormatInfo
{
    public required int SampleRateHz { get; init; }
    public required int BitsPerSample { get; init; }
    public required int Channels { get; init; }
    public required string Encoding { get; init; }

    /// <summary>What each channel is (speaker position for outputs, "Ch n" for inputs).</summary>
    public IReadOnlyList<SpeakerChannel> ChannelLayout { get; init; } = Array.Empty<SpeakerChannel>();

    public override string ToString() => $"{SampleRateHz:N0} Hz, {BitsPerSample}-bit, {Channels} ch ({Encoding})";
}
