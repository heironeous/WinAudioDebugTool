namespace AudioDeviceDebugger.Models;

/// <summary>
/// The audio captured by a mic test, kept so it can be replayed — whole, or one input channel at a time — and
/// compared with recordings made in other processing modes.
/// </summary>
public sealed class RecordedTake
{
    public RecordedTake(float[] interleaved, int channels, int sampleRate, CaptureMode mode = CaptureMode.Standard,
        IReadOnlyList<AudioEffectInfo>? effects = null, int glitches = 0)
    {
        Interleaved = interleaved;
        Channels = channels;
        SampleRate = sampleRate;
        Mode = mode;
        Effects = effects;
        Glitches = glitches;
        RecordedAt = DateTime.Now;

        var power = new double[channels];
        var peak = new double[channels];
        for (int i = 0; i < interleaved.Length; i++)
        {
            int c = i % channels;
            double s = interleaved[i];
            power[c] += s * s;
            peak[c] = Math.Max(peak[c], Math.Abs(s));
        }
        int frames = Math.Max(1, Frames);
        ChannelRmsDb = power.Select(p => LevelScale.ToDb(Math.Sqrt(p / frames))).ToArray();
        ChannelPeakDb = peak.Select(LevelScale.ToDb).ToArray();
        RmsDb = LevelScale.ToDb(Math.Sqrt(power.Sum() / ((double)frames * channels)));
        PeakDb = ChannelPeakDb.Count == 0 ? double.NegativeInfinity : ChannelPeakDb.Max();
    }

    /// <summary>Samples, interleaved by channel, as 32-bit float.</summary>
    public float[] Interleaved { get; }
    public int Channels { get; }
    public int SampleRate { get; }

    /// <summary>The processing mode the recording asked Windows for.</summary>
    public CaptureMode Mode { get; }

    /// <summary>Effects Windows reported for the recording's stream; null if they weren't (or couldn't be) checked.</summary>
    public IReadOnlyList<AudioEffectInfo>? Effects { get; }

    /// <summary>Gaps the audio engine reported while recording (audio lost because it wasn't read in time).</summary>
    public int Glitches { get; }

    public DateTime RecordedAt { get; }

    /// <summary>RMS level over all channels, in dBFS.</summary>
    public double RmsDb { get; }

    /// <summary>Highest sample on any channel, in dBFS.</summary>
    public double PeakDb { get; }

    public IReadOnlyList<double> ChannelRmsDb { get; }
    public IReadOnlyList<double> ChannelPeakDb { get; }

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
