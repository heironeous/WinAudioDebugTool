using NAudio.Wave;

namespace AudioDeviceDebugger.Services;

/// <summary>
/// Plays a sequence of mono segments, each tagged with a channel, then ends. Each segment is written either to its
/// own channel only (speaker tests) or to every output channel (listening to one mic channel on all speakers).
/// <see cref="ChannelStarted"/> fires as each segment begins (and with null at the end) so the UI can highlight it.
/// </summary>
internal sealed class ChannelSegmentsSampleProvider : ISampleProvider
{
    public sealed record Segment(int Channel, float[] Mono, int GapFramesAfter);

    private readonly IReadOnlyList<Segment> _segments;
    private readonly int _outputChannels;
    private readonly bool _routeToAllChannels;
    private int _segmentIndex;
    private int _framePosition;
    private bool _announced;

    public ChannelSegmentsSampleProvider(IReadOnlyList<Segment> segments, int sampleRate, int outputChannels, bool routeToAllChannels)
    {
        _segments = segments;
        _outputChannels = outputChannels;
        _routeToAllChannels = routeToAllChannels;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, outputChannels);
        TotalFrames = segments.Sum(s => s.Mono.Length + s.GapFramesAfter);
    }

    public WaveFormat WaveFormat { get; }

    public int TotalFrames { get; }

    /// <summary>Raised on the audio thread when a segment starts (its channel) and once at the end (null).</summary>
    public event Action<int?>? ChannelStarted;

    public int Read(float[] buffer, int offset, int count)
    {
        int framesWanted = count / _outputChannels;
        int framesWritten = 0;

        while (framesWritten < framesWanted && _segmentIndex < _segments.Count)
        {
            var segment = _segments[_segmentIndex];
            if (!_announced)
            {
                _announced = true;
                ChannelStarted?.Invoke(segment.Channel);
            }

            int segmentFrames = segment.Mono.Length + segment.GapFramesAfter;
            int frames = Math.Min(framesWanted - framesWritten, segmentFrames - _framePosition);
            for (int f = 0; f < frames; f++)
            {
                int source = _framePosition + f;
                float sample = source < segment.Mono.Length ? segment.Mono[source] : 0f;
                int o = offset + (framesWritten + f) * _outputChannels;
                for (int c = 0; c < _outputChannels; c++)
                {
                    buffer[o + c] = _routeToAllChannels || c == segment.Channel ? sample : 0f;
                }
            }

            framesWritten += frames;
            _framePosition += frames;
            if (_framePosition >= segmentFrames)
            {
                _segmentIndex++;
                _framePosition = 0;
                _announced = false;
                if (_segmentIndex == _segments.Count)
                {
                    ChannelStarted?.Invoke(null);
                }
            }
        }

        return framesWritten * _outputChannels;
    }
}
