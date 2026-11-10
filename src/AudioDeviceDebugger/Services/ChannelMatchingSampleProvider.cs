using NAudio.Wave;

namespace AudioDeviceDebugger.Services;

/// <summary>
/// Converts a sample stream to a different channel count by averaging every input channel into one signal and
/// writing it to every output channel. Good enough for test playback of any mic layout (mono, stereo, 4-channel
/// arrays…) on any speaker layout; it is not a proper surround downmix.
/// </summary>
internal sealed class ChannelMatchingSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _inputChannels;
    private readonly int _outputChannels;
    private float[] _sourceBuffer = Array.Empty<float>();

    public ChannelMatchingSampleProvider(ISampleProvider source, int outputChannels)
    {
        _source = source;
        _inputChannels = source.WaveFormat.Channels;
        _outputChannels = outputChannels;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, outputChannels);
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        int frames = count / _outputChannels;
        int samplesNeeded = frames * _inputChannels;
        if (_sourceBuffer.Length < samplesNeeded)
        {
            _sourceBuffer = new float[samplesNeeded];
        }

        int framesRead = _source.Read(_sourceBuffer, 0, samplesNeeded) / _inputChannels;
        for (int frame = 0; frame < framesRead; frame++)
        {
            float sum = 0;
            for (int c = 0; c < _inputChannels; c++)
            {
                sum += _sourceBuffer[frame * _inputChannels + c];
            }
            float mixed = sum / _inputChannels;

            int outBase = offset + frame * _outputChannels;
            for (int c = 0; c < _outputChannels; c++)
            {
                buffer[outBase + c] = mixed;
            }
        }
        return framesRead * _outputChannels;
    }
}
