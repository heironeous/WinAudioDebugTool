namespace AudioDeviceDebugger.Models;

public sealed class AudioFormatInfo
{
    public required int SampleRateHz { get; init; }
    public required int BitsPerSample { get; init; }
    public required int Channels { get; init; }
    public required string Encoding { get; init; }

    public override string ToString() => $"{SampleRateHz:N0} Hz, {BitsPerSample}-bit, {Channels} ch ({Encoding})";
}
