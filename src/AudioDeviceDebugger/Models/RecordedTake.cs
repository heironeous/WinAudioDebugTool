namespace AudioDeviceDebugger.Models;

/// <summary>
/// The audio captured by the last mic test of a device, kept so each input channel can be listened to separately.
/// </summary>
public sealed class RecordedTake
{
    public RecordedTake(float[] interleaved, int channels, int sampleRate)
    {
        Interleaved = interleaved;
        Channels = channels;
        SampleRate = sampleRate;
    }

    /// <summary>Samples, interleaved by channel, as 32-bit float.</summary>
    public float[] Interleaved { get; }
    public int Channels { get; }
    public int SampleRate { get; }

    public int Frames => Interleaved.Length / Channels;
    public TimeSpan Duration => TimeSpan.FromSeconds((double)Frames / SampleRate);

    /// <summary>Copies one channel out as mono samples.</summary>
    public float[] GetChannel(int channel)
    {
        var mono = new float[Frames];
        for (int f = 0; f < mono.Length; f++)
        {
            mono[f] = Interleaved[f * Channels + channel];
        }
        return mono;
    }
}
