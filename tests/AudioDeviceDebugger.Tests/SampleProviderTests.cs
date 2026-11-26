using AudioDeviceDebugger.Models;
using AudioDeviceDebugger.Services;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace AudioDeviceDebugger.Tests;

public class SampleProviderTests
{
    /// <summary>Plays a fixed array of interleaved samples, then ends.</summary>
    private sealed class ArraySource : ISampleProvider
    {
        private readonly float[] _samples;
        private int _position;

        public ArraySource(int channels, params float[] samples)
        {
            _samples = samples;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, channels);
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            int n = Math.Min(count, _samples.Length - _position);
            Array.Copy(_samples, _position, buffer, offset, n);
            _position += n;
            return n;
        }
    }

    private static float[] ReadAll(ISampleProvider provider, int bufferSize = 64)
    {
        var all = new List<float>();
        var buffer = new float[bufferSize];
        int read;
        while ((read = provider.Read(buffer, 0, buffer.Length)) > 0)
        {
            all.AddRange(buffer.Take(read));
        }
        return all.ToArray();
    }

    [Fact]
    public void Stereo_is_averaged_into_mono()
    {
        var provider = new ChannelMatchingSampleProvider(new ArraySource(2, 1f, 0f, 0.5f, 0.5f, -1f, 1f), 1);

        Assert.Equal(1, provider.WaveFormat.Channels);
        Assert.Equal(new[] { 0.5f, 0.5f, 0f }, ReadAll(provider));
    }

    [Fact]
    public void Mono_is_copied_to_every_output_channel()
    {
        var provider = new ChannelMatchingSampleProvider(new ArraySource(1, 0.25f, -0.5f), 4);

        Assert.Equal(new[] { 0.25f, 0.25f, 0.25f, 0.25f, -0.5f, -0.5f, -0.5f, -0.5f }, ReadAll(provider));
    }

    [Fact]
    public void A_four_channel_mic_plays_on_stereo_speakers()
    {
        var provider = new ChannelMatchingSampleProvider(new ArraySource(4, 1f, 1f, 0f, 0f), 2);

        Assert.Equal(48000, provider.WaveFormat.SampleRate);
        Assert.Equal(new[] { 0.5f, 0.5f }, ReadAll(provider, bufferSize: 2));
    }

    [Theory]
    [InlineData(DeviceState.Active, DeviceConnectionState.Active)]
    [InlineData(DeviceState.Disabled, DeviceConnectionState.Disabled)]
    [InlineData(DeviceState.NotPresent, DeviceConnectionState.NotPresent)]
    [InlineData(DeviceState.Unplugged, DeviceConnectionState.Unplugged)]
    public void Endpoint_states_map_to_the_apps_states(DeviceState state, DeviceConnectionState expected) =>
        Assert.Equal(expected, DeviceEnumerationService.MapState(state));
}
