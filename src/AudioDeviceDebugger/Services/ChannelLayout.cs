using System.IO;
using AudioDeviceDebugger.Models;
using NAudio.Wave;

namespace AudioDeviceDebugger.Services;

/// <summary>Names the channels of an endpoint's mix format from its WAVEFORMATEXTENSIBLE speaker mask.</summary>
public static class ChannelLayout
{
    // KSAUDIO_SPEAKER_* / SPEAKER_* bits, in the order channels appear in the interleaved stream.
    private static readonly (uint Bit, string Short, string Long)[] SpeakerPositions =
    {
        (0x1, "FL", "Front Left"),
        (0x2, "FR", "Front Right"),
        (0x4, "C", "Centre"),
        (0x8, "LFE", "Low-Frequency Effects (subwoofer)"),
        (0x10, "BL", "Back Left"),
        (0x20, "BR", "Back Right"),
        (0x40, "FLC", "Front Left of Centre"),
        (0x80, "FRC", "Front Right of Centre"),
        (0x100, "BC", "Back Centre"),
        (0x200, "SL", "Side Left"),
        (0x400, "SR", "Side Right"),
        (0x800, "TC", "Top Centre"),
        (0x1000, "TFL", "Top Front Left"),
        (0x2000, "TFC", "Top Front Centre"),
        (0x4000, "TFR", "Top Front Right"),
        (0x8000, "TBL", "Top Back Left"),
        (0x10000, "TBC", "Top Back Centre"),
        (0x20000, "TBR", "Top Back Right"),
    };

    /// <summary>
    /// Output devices: speaker names from the channel mask when it describes every channel; otherwise (and for
    /// input devices — a mic array's mask doesn't name physical capsules) generic "Ch n" names.
    /// </summary>
    public static IReadOnlyList<SpeakerChannel> Describe(WaveFormat format, bool isOutput)
    {
        uint mask = isOutput ? GetChannelMask(format) : 0;
        var positions = SpeakerPositions.Where(p => (mask & p.Bit) != 0).ToList();

        var channels = new List<SpeakerChannel>(format.Channels);
        for (int i = 0; i < format.Channels; i++)
        {
            if (positions.Count == format.Channels)
            {
                var p = positions[i];
                channels.Add(new SpeakerChannel(i, p.Short, p.Long, IsLfe: p.Bit == 0x8));
            }
            else if (isOutput && format.Channels == 1)
            {
                channels.Add(new SpeakerChannel(i, "M", "Mono", IsLfe: false));
            }
            else
            {
                channels.Add(new SpeakerChannel(i, $"Ch {i + 1}", isOutput ? $"Channel {i + 1}" : $"Input channel {i + 1}", IsLfe: false));
            }
        }
        return channels;
    }

    /// <summary>Speaker positions in a channel mask, e.g. 0x3F → "FL FR C LFE BL BR"; unknown bits are listed in hex.</summary>
    public static string DescribeMask(uint mask)
    {
        if (mask == 0) return "none";
        var names = SpeakerPositions.Where(p => (mask & p.Bit) != 0).Select(p => p.Short).ToList();
        uint known = SpeakerPositions.Aggregate(0u, (all, p) => all | p.Bit);
        if ((mask & ~known) != 0) names.Add($"0x{mask & ~known:X}");
        return string.Join(" ", names);
    }

    /// <summary>dwChannelMask of a WAVEFORMATEXTENSIBLE (0 for plain formats). NAudio doesn't expose the field, so
    /// it's read from NAudio's serialised form: a 4-byte length prefix, then WAVEFORMATEX (18 bytes),
    /// wValidBitsPerSample (2), dwChannelMask (4) — i.e. the mask is at byte offset 24.</summary>
    private static uint GetChannelMask(WaveFormat format)
    {
        if (format.Encoding != WaveFormatEncoding.Extensible) return 0;
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            format.Serialize(writer);
        }
        var bytes = stream.ToArray();
        return bytes.Length >= 28 ? BitConverter.ToUInt32(bytes, 24) : 0;
    }
}
