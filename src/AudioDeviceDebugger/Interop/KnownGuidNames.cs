namespace AudioDeviceDebugger.Interop;

/// <summary>
/// Names for audio-related GUID constants — jack/node types (KSNODETYPE_*), categories, sample-format subtypes,
/// signal-processing modes, effect types, audio interface classes. The table itself is generated from the Windows SDK
/// metadata into <c>KnownGuidNames.g.cs</c> (see tools/PropertyKeyNameGenerator).
/// </summary>
internal static partial class KnownGuidNames
{
    // Longest first, so "KSNODETYPE_" isn't left behind by a shorter match.
    private static readonly string[] Prefixes =
    {
        "AUDIO_SIGNALPROCESSINGMODE_", "KSDATAFORMAT_SUBTYPE_", "DEVINTERFACE_AUDIO_", "AUDIO_EFFECT_TYPE_",
        "KSNODETYPE_", "KSCATEGORY_",
    };

    // Words kept in capitals when a symbol is turned into text.
    private static readonly HashSet<string> Acronyms = new(StringComparer.Ordinal)
    {
        "HDMI", "SPDIF", "USB", "DSP", "PCM", "IEEE", "ADC", "DAC", "AGC", "AEC", "MPEG", "MPEG2", "DTS", "DTSX", "WMA",
        "AC3", "ATRAC", "AAC", "MIDI", "DRM", "DVD", "CD", "TV", "VCR", "DAT", "DCC", "DSS", "SDDS", "LFE", "3D", "1394",
        "DV", "DLS", "SYSEX", "EAC3", "MLP", "ALAW", "MULAW", "ADPCM", "DSD", "FM", "UMP", "WAVEFORMATEX", "SRC",
    };

    /// <summary>The SDK symbol for a GUID, e.g. "KSNODETYPE_HEADPHONES", or null if it isn't in the table.</summary>
    public static string? TryGetSymbol(Guid id) => SdkNames.TryGetValue(id, out var name) ? name : null;

    /// <summary>A readable name for a GUID, e.g. "Headphones" for KSNODETYPE_HEADPHONES; null if unknown.</summary>
    public static string? Describe(Guid id) => TryGetSymbol(id) is { } symbol ? Humanize(symbol) : null;

    /// <summary>"KSNODETYPE_COMMUNICATION_SPEAKER" → "Communication speaker", "KSNODETYPE_HDMI_INTERFACE" → "HDMI
    /// interface".</summary>
    public static string Humanize(string symbol)
    {
        string body = Prefixes.FirstOrDefault(p => symbol.StartsWith(p, StringComparison.Ordinal)) is { } prefix
            ? symbol[prefix.Length..]
            : symbol;
        var words = body.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => Acronyms.Contains(w) || w.Any(char.IsDigit) ? w : w.ToLowerInvariant())
            .ToArray();
        if (words.Length == 0) return symbol;
        words[0] = char.ToUpperInvariant(words[0][0]) + words[0][1..];
        return string.Join(" ", words);
    }
}
