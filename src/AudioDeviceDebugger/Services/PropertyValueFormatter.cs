using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AudioDeviceDebugger.Interop;

namespace AudioDeviceDebugger.Services;

/// <summary>A property value made readable: a one-line summary, plus optional detail lines for the tooltip.</summary>
public sealed record DecodedValue(string Summary, string? Detail = null);

/// <summary>
/// Turns raw property-store values into readable text for the PROPERTIES tab. Two kinds of decoding:
/// <list type="bullet">
/// <item>By key, for documented properties whose value has a defined meaning: form factor, jack type, channel
/// masks, the 16.16 fixed-point default volume, the microphone-array geometry (KSAUDIO_MIC_ARRAY_GEOMETRY).</item>
/// <item>By shape, for any value that is unambiguously recognisable: wave formats (WAVEFORMATEX / -EXTENSIBLE),
/// VS_FIXEDFILEINFO version blocks, SYSTEMTIME, UTF-16 text in a blob, known GUIDs, and endpoint IDs (resolved to
/// the endpoint's name).</item>
/// </list>
/// Shape decoding never claims what an undocumented property *means* — only what its value contains.
/// </summary>
public static class PropertyValueFormatter
{
    private static readonly Regex EndpointId = new(
        @"\{0\.0\.[01]\.00000000\}\.\{[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\}",
        RegexOptions.Compiled);

    /// <summary>The value as NAudio returns it, as text (blobs as hex) — what the tab showed before decoding.</summary>
    public static string FormatRaw(object? value) => value switch
    {
        null => "(null)",
        byte[] bytes when bytes.Length <= 64 => $"byte[{bytes.Length}] {Convert.ToHexString(bytes)}",
        byte[] bytes => $"byte[{bytes.Length}] {Convert.ToHexString(bytes, 0, 64)}…",
        string[] strings => string.Join("; ", strings),
        _ => value.ToString() ?? "(null)",
    };

    /// <summary>
    /// A readable form of <paramref name="value"/>, or null when there's nothing better than the raw text.
    /// <paramref name="endpointName"/> resolves an endpoint ID to its friendly name (null if unknown).
    /// </summary>
    public static DecodedValue? Decode(string? symbol, object? value, Func<string, string?> endpointName)
    {
        try
        {
            return DecodeByKey(symbol, value) ?? DecodeByShape(value, endpointName);
        }
        catch
        {
            return null; // a decoder must never break the property list
        }
    }

    // ---- by key -------------------------------------------------------------------------------------------------

    private static DecodedValue? DecodeByKey(string? symbol, object? value) => symbol switch
    {
        "PKEY_AudioEndpoint_FormFactor" when AsUInt(value) is { } v => new(FormFactor(v)),
        "PKEY_AudioEndpoint_JackSubType" or "PKEY_AudioEndpoint_Association" when AsGuid(value) is { } g =>
            g == Guid.Empty ? new("None") : KnownGuidNames.Describe(g) is { } name ? new(name) : null,
        "PKEY_AudioEndpoint_PhysicalSpeakers" or "PKEY_AudioEndpoint_FullRangeSpeakers" when AsUInt(value) is { } mask =>
            new($"{ChannelLayout.DescribeMask(mask)} (0x{mask:X})"),
        "PKEY_AudioEndpoint_Disable_SysFx" when AsUInt(value) is { } v =>
            new(v == 0 ? "No — audio enhancements are on" : v == 1 ? "Yes — audio enhancements are off" : v.ToString()),
        "PKEY_AudioEndpoint_Supports_EventDriven_Mode" when AsUInt(value) is { } v =>
            new(v == 1 ? "Yes" : v == 0 ? "No" : v.ToString()),
        // 16.16 fixed point, upper 16 bits signed (Microsoft Learn: PKEY_AudioEndpoint_Default/Max_VolumeInDb).
        "PKEY_AudioEndpoint_Default_VolumeInDb" or "PKEY_AudioEndpoint_Max_VolumeInDb" when AsUInt(value) is { } v =>
            new($"{(int)v / 65536.0:0.##} dB"),
        "PKEY_Devices_MicrophoneArray_Geometry" when value is byte[] blob => MicArrayGeometry(blob),
        _ => null,
    };

    private static string FormFactor(uint value) => value switch
    {
        0 => "Remote network device",
        1 => "Speakers",
        2 => "Line level",
        3 => "Headphones",
        4 => "Microphone",
        5 => "Headset",
        6 => "Handset",
        7 => "Unknown digital passthrough",
        8 => "S/PDIF",
        9 => "Digital audio display device (HDMI / DisplayPort)",
        10 => "Unknown form factor",
        _ => $"Unknown ({value})",
    } + $" ({value})";

    /// <summary>KSAUDIO_MIC_ARRAY_GEOMETRY: angles in 1/10000 radian, coordinates in mm, frequencies in Hz.</summary>
    private static DecodedValue? MicArrayGeometry(byte[] b)
    {
        if (b.Length < 18) return null;
        int micCount = U16(b, 16);
        if (b.Length < 18 + micCount * 12) return null;

        string arrayType = U16(b, 2) switch { 0 => "Linear", 1 => "Planar", 2 => "3D", var t => $"Type {t}" };
        static string Deg(short tenThousandthsOfRadian) =>
            $"{tenThousandthsOfRadian / 10000.0 * 180 / Math.PI:0}°";
        static string MicType(int t) => (t & 0xF) switch
        {
            0 => "omnidirectional", 1 => "subcardioid", 2 => "cardioid", 3 => "supercardioid", 4 => "hypercardioid",
            5 => "8-shaped", 15 => "vendor-defined", _ => $"type {t}",
        };

        var mics = Enumerable.Range(0, micCount).Select(i =>
        {
            int o = 18 + i * 12;
            return (Type: MicType(U16(b, o)), X: S16(b, o + 2), Y: S16(b, o + 4), Z: S16(b, o + 6),
                Vertical: S16(b, o + 8), Horizontal: S16(b, o + 10));
        }).ToList();

        string types = mics.Select(m => m.Type).Distinct().Count() == 1 ? mics[0].Type + " " : "";
        string summary = $"{arrayType} array · {micCount} {types}mics · {U16(b, 12)}–{U16(b, 14)} Hz · at "
            + string.Join(" ", mics.Select(m => $"({m.X},{m.Y},{m.Z})")) + " mm";

        var detail = new StringBuilder();
        detail.AppendLine($"Version {U16(b, 0) >> 8}.{U16(b, 0) & 0xFF}, {arrayType.ToLowerInvariant()} array of {micCount} microphones");
        detail.AppendLine($"Frequency band {U16(b, 12)}–{U16(b, 14)} Hz");
        detail.AppendLine($"Working volume: vertical {Deg(S16(b, 4))} to {Deg(S16(b, 6))}, horizontal {Deg(S16(b, 8))} to {Deg(S16(b, 10))}");
        for (int i = 0; i < mics.Count; i++)
        {
            var m = mics[i];
            detail.AppendLine($"Mic {i + 1}: {m.Type} at x={m.X} y={m.Y} z={m.Z} mm, vertical angle {Deg(m.Vertical)}, horizontal angle {Deg(m.Horizontal)}");
        }
        detail.Append("x = towards the user, y = to the user's right, z = up");
        return new DecodedValue(summary, detail.ToString());
    }

    // ---- by shape -----------------------------------------------------------------------------------------------

    private static DecodedValue? DecodeByShape(object? value, Func<string, string?> endpointName) => value switch
    {
        DateTime time => new(time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
        Guid guid => KnownGuidNames.Describe(guid) is { } name ? new(name) : null,
        string text => DecodeString(text, endpointName),
        byte[] blob => WaveFormat(blob) ?? FileVersion(blob) ?? SystemTime(blob) ?? Utf16Text(blob),
        _ => null,
    };

    private static DecodedValue? DecodeString(string text, Func<string, string?> endpointName)
    {
        if (Guid.TryParse(text, out var guid) && text.StartsWith('{') && KnownGuidNames.Describe(guid) is { } name)
        {
            return new(name);
        }
        var match = EndpointId.Match(text);
        if (match.Success && endpointName(match.Value) is { } endpoint)
        {
            return new(match.Value == text ? endpoint : $"{text}  → {endpoint}");
        }
        return null;
    }

    /// <summary>WAVEFORMATEX (PCM / IEEE float) or WAVEFORMATEXTENSIBLE, only when every field is consistent.</summary>
    private static DecodedValue? WaveFormat(byte[] b)
    {
        if (b.Length < 16) return null;
        int tag = U16(b, 0), channels = U16(b, 2), blockAlign = U16(b, 12), bits = U16(b, 14);
        uint rate = U32(b, 4), bytesPerSecond = U32(b, 8);
        if (tag is not (1 or 3 or 0xFFFE)) return null;
        if (channels is < 1 or > 64 || rate is < 1000 or > 1_536_000 || bits is not (8 or 16 or 20 or 24 or 32 or 64)) return null;
        if (blockAlign != channels * bits / 8 || bytesPerSecond != rate * (uint)blockAlign) return null;

        string encoding = tag == 3 ? "float" : "PCM";
        string bitText = $"{bits}-bit";
        string channelText = $"{channels} ch";
        if (tag == 0xFFFE)
        {
            if (b.Length < 40 || U16(b, 16) < 22) return null;
            int validBits = U16(b, 18);
            uint mask = U32(b, 20);
            var subFormat = new Guid(b.AsSpan(24, 16));
            encoding = KnownGuidNames.TryGetSymbol(subFormat) switch
            {
                "KSDATAFORMAT_SUBTYPE_PCM" => "PCM",
                "KSDATAFORMAT_SUBTYPE_IEEE_FLOAT" => "float",
                { } other => KnownGuidNames.Humanize(other),
                null => subFormat.ToString("B"),
            };
            if (validBits != 0 && validBits != bits) bitText = $"{validBits}-bit in {bits}-bit";
            if (mask != 0) channelText += $" ({ChannelLayout.DescribeMask(mask)})";
        }
        string khz = (rate / 1000.0).ToString("0.###", CultureInfo.InvariantCulture);
        return new($"{khz} kHz · {bitText} {encoding} · {channelText}",
            tag == 0xFFFE ? "Wave format (WAVEFORMATEXTENSIBLE)" : "Wave format (WAVEFORMATEX)");
    }

    /// <summary>VS_FIXEDFILEINFO (signature 0xFEEF04BD): file and product version.</summary>
    private static DecodedValue? FileVersion(byte[] b)
    {
        if (b.Length < 52 || U32(b, 0) != 0xFEEF04BD) return null;
        static string Version(uint ms, uint ls) => $"{ms >> 16}.{ms & 0xFFFF}.{ls >> 16}.{ls & 0xFFFF}";
        string file = Version(U32(b, 8), U32(b, 12)), product = Version(U32(b, 16), U32(b, 20));
        return new(file == product ? $"Version {file}" : $"File version {file}, product version {product}",
            "Version information block (VS_FIXEDFILEINFO)");
    }

    /// <summary>A 16-byte SYSTEMTIME with every field in range.</summary>
    private static DecodedValue? SystemTime(byte[] b)
    {
        if (b.Length != 16) return null;
        int year = U16(b, 0), month = U16(b, 2), dayOfWeek = U16(b, 4), day = U16(b, 6);
        int hour = U16(b, 8), minute = U16(b, 10), second = U16(b, 12), ms = U16(b, 14);
        if (year is < 1980 or > 2100 || month is < 1 or > 12 || dayOfWeek > 6 || day is < 1 or > 31
            || hour > 23 || minute > 59 || second > 59 || ms > 999)
        {
            return null;
        }
        return new($"{year:0000}-{month:00}-{day:00} {hour:00}:{minute:00}:{second:00}",
            "Date and time (SYSTEMTIME; time zone not stated — often UTC)");
    }

    /// <summary>
    /// UTF-16 text at the start of a blob: either text and zero padding only, or a fixed-size text field (text, then
    /// at least 32 zero bytes) followed by other data. Only plain Latin text counts — binary data that happens to form
    /// valid UTF-16 (usually CJK-looking) is rejected.
    /// </summary>
    private static DecodedValue? Utf16Text(byte[] b)
    {
        if (b.Length < 6) return null;
        int chars = b.Length / 2;
        var text = new StringBuilder();
        int i = 0;
        for (; i < chars; i++)
        {
            char c = (char)BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(i * 2));
            if (c == '\0') break;
            if (!IsPlainTextChar(c)) return null;
            text.Append(c);
        }
        if (text.Length < 3 || !text.ToString().Any(char.IsLetter)) return null;

        int zerosStart = i * 2;
        int zeros = 0;
        while (zerosStart + zeros < b.Length && b[zerosStart + zeros] == 0) zeros++;
        int rest = b.Length - zerosStart - zeros;
        if (rest == 0) return new($"\"{text}\"", "Text (UTF-16) stored as binary");
        if (zeros < 32) return null;
        return new($"\"{text}\"  (+ {rest} bytes of other data)",
            $"Text (UTF-16) in a fixed-size field, followed by {rest} bytes of other data");

        static bool IsPlainTextChar(char c) =>
            c is >= ' ' and <= '~' or >= ' ' and <= 'ſ' or '™' or '–' or '—' or '’';
    }

    // ---- helpers ------------------------------------------------------------------------------------------------

    private static uint? AsUInt(object? value) => value switch
    {
        uint u => u,
        int i => unchecked((uint)i),
        ushort s => s,
        short s => unchecked((uint)s),
        byte by => by,
        _ => null,
    };

    private static Guid? AsGuid(object? value) => value switch
    {
        Guid g => g,
        string s when Guid.TryParse(s, out var g) => g,
        _ => null,
    };

    private static int U16(byte[] b, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(offset));
    private static short S16(byte[] b, int offset) => BinaryPrimitives.ReadInt16LittleEndian(b.AsSpan(offset));
    private static uint U32(byte[] b, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(offset));
}
