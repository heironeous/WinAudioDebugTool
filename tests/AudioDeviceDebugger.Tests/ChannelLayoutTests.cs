using AudioDeviceDebugger.Models;
using AudioDeviceDebugger.Services;
using NAudio.Wave;

namespace AudioDeviceDebugger.Tests;

public class ChannelLayoutTests
{
    [Fact]
    public void Output_with_a_speaker_mask_gets_speaker_names()
    {
        // NAudio's WaveFormatExtensible sets the mask to the first n speaker positions: 0x3F for 5.1.
        var channels = ChannelLayout.Describe(new WaveFormatExtensible(48000, 32, 6), isOutput: true);

        Assert.Equal(new[] { "FL", "FR", "C", "LFE", "BL", "BR" }, channels.Select(c => c.ShortName));
        Assert.Equal("Low-Frequency Effects (subwoofer)", channels[3].LongName);
        Assert.Equal(new[] { 3 }, channels.Where(c => c.IsLfe).Select(c => c.Index));
    }

    [Fact]
    public void Input_channels_are_numbered_even_with_a_mask()
    {
        // A mic array's mask doesn't name its capsules.
        var channels = ChannelLayout.Describe(new WaveFormatExtensible(48000, 32, 4), isOutput: false);

        Assert.Equal(new[] { "Ch 1", "Ch 2", "Ch 3", "Ch 4" }, channels.Select(c => c.ShortName));
        Assert.Equal("Input channel 3", channels[2].LongName);
        Assert.DoesNotContain(channels, c => c.IsLfe);
    }

    [Fact]
    public void Plain_mono_output_is_called_mono()
    {
        var channels = ChannelLayout.Describe(new WaveFormat(44100, 16, 1), isOutput: true);

        var only = Assert.Single(channels);
        Assert.Equal(new SpeakerChannel(0, "M", "Mono", IsLfe: false), only);
    }

    [Fact]
    public void Plain_stereo_output_without_a_mask_is_numbered()
    {
        var channels = ChannelLayout.Describe(new WaveFormat(44100, 16, 2), isOutput: true);

        Assert.Equal(new[] { "Channel 1", "Channel 2" }, channels.Select(c => c.LongName));
    }

    [Theory]
    [InlineData(0u, "none")]
    [InlineData(0x3u, "FL FR")]
    [InlineData(0x3Fu, "FL FR C LFE BL BR")]
    [InlineData(0x60Fu, "FL FR C LFE SL SR")]
    [InlineData(0x80000003u, "FL FR 0x80000000")]
    public void DescribeMask_lists_positions_and_unknown_bits(uint mask, string expected) =>
        Assert.Equal(expected, ChannelLayout.DescribeMask(mask));
}
