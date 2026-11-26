using System.Buffers.Binary;
using System.Text;
using AudioDeviceDebugger.Services;

namespace AudioDeviceDebugger.Tests;

public class PropertyValueFormatterTests
{
    private static string? NoEndpoints(string id) => null;

    private static string? Summary(string? symbol, object? value, Func<string, string?>? endpointName = null) =>
        PropertyValueFormatter.Decode(symbol, value, endpointName ?? NoEndpoints)?.Summary;

    /// <summary>Little-endian blob builder.</summary>
    private sealed class Blob
    {
        private readonly List<byte> _bytes = new();
        public Blob U16(int v) { var b = new byte[2]; BinaryPrimitives.WriteUInt16LittleEndian(b, (ushort)v); _bytes.AddRange(b); return this; }
        public Blob U32(uint v) { var b = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(b, v); _bytes.AddRange(b); return this; }
        public Blob Bytes(params byte[] b) { _bytes.AddRange(b); return this; }
        public Blob Zeros(int n) { _bytes.AddRange(new byte[n]); return this; }
        public byte[] ToArray() => _bytes.ToArray();
    }

    // ---- by key ---------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(5u, "Headset (5)")]
    [InlineData(9u, "Digital audio display device (HDMI / DisplayPort) (9)")]
    [InlineData(42u, "Unknown (42)")]
    public void Form_factor(uint value, string expected) =>
        Assert.Equal(expected, Summary("PKEY_AudioEndpoint_FormFactor", value));

    [Fact]
    public void Speaker_mask() =>
        Assert.Equal("FL FR C LFE BL BR (0x3F)", Summary("PKEY_AudioEndpoint_PhysicalSpeakers", 0x3Fu));

    [Fact]
    public void Enhancements_switch() =>
        Assert.Equal("Yes — audio enhancements are off", Summary("PKEY_AudioEndpoint_Disable_SysFx", 1u));

    [Theory]
    [InlineData(-6 * 65536, "-6 dB")]
    [InlineData(65536 / 2, "0.5 dB")]
    public void Volume_in_dB_is_16_16_fixed_point(int value, string expected) =>
        Assert.Equal(expected, Summary("PKEY_AudioEndpoint_Default_VolumeInDb", value));

    [Fact]
    public void Mic_array_geometry()
    {
        // Version 1.0, linear, vertical −90°…90° and horizontal 0° (1/10000 rad), 80–8000 Hz, two omni mics.
        var blob = new Blob().U16(0x0100).U16(0).U16(unchecked((ushort)-15708)).U16(15708).U16(0).U16(0)
            .U16(80).U16(8000).U16(2)
            .U16(0).U16(0).U16(unchecked((ushort)-30)).U16(0).U16(0).U16(0)
            .U16(0).U16(0).U16(30).U16(0).U16(0).U16(0)
            .ToArray();

        var decoded = PropertyValueFormatter.Decode("PKEY_Devices_MicrophoneArray_Geometry", blob, NoEndpoints);

        Assert.NotNull(decoded);
        Assert.Equal("Linear array · 2 omnidirectional mics · 80–8000 Hz · at (0,-30,0) (0,30,0) mm", decoded.Summary);
        Assert.Contains("vertical -90° to 90°", decoded.Detail);
    }

    [Fact]
    public void Truncated_mic_array_geometry_is_not_decoded() =>
        Assert.Null(Summary("PKEY_Devices_MicrophoneArray_Geometry", new Blob().Zeros(16).U16(3).ToArray()));

    // ---- by shape -------------------------------------------------------------------------------------------------

    [Fact]
    public void Wave_format_blob()
    {
        var blob = new Blob().U16(1).U16(2).U32(48000).U32(192000).U16(4).U16(16).ToArray();

        Assert.Equal("48 kHz · 16-bit PCM · 2 ch", Summary(null, blob));
    }

    [Fact]
    public void Extensible_float_wave_format_with_a_mask()
    {
        var floatSubtype = new Guid("00000003-0000-0010-8000-00aa00389b71"); // KSDATAFORMAT_SUBTYPE_IEEE_FLOAT
        var blob = new Blob().U16(0xFFFE).U16(2).U32(44100).U32(352800).U16(8).U16(32).U16(22)
            .U16(32).U32(0x3).Bytes(floatSubtype.ToByteArray()).ToArray();

        Assert.Equal("44.1 kHz · 32-bit float · 2 ch (FL FR)", Summary(null, blob));
    }

    [Fact]
    public void Inconsistent_wave_format_is_not_decoded()
    {
        // The block alignment doesn't match 2 ch × 16 bit.
        var blob = new Blob().U16(1).U16(2).U32(48000).U32(192000).U16(3).U16(16).ToArray();

        Assert.Null(Summary(null, blob));
    }

    [Fact]
    public void Version_block()
    {
        var blob = new Blob().U32(0xFEEF04BD).U32(0x10000).U32(10 << 16).U32((19041u << 16) | 1)
            .U32(10 << 16).U32((19041u << 16) | 1).Zeros(28).ToArray();

        Assert.Equal("Version 10.0.19041.1", Summary(null, blob));
    }

    [Fact]
    public void System_time()
    {
        var blob = new Blob().U16(2026).U16(11).U16(3).U16(25).U16(10).U16(20).U16(30).U16(0).ToArray();

        Assert.Equal("2026-11-25 10:20:30", Summary(null, blob));
    }

    [Fact]
    public void Utf16_text_with_zero_padding()
    {
        var blob = new Blob().Bytes(Encoding.Unicode.GetBytes("Jabra Link 390")).Zeros(10).ToArray();

        Assert.Equal("\"Jabra Link 390\"", Summary(null, blob));
    }

    [Fact]
    public void Utf16_text_field_followed_by_other_data()
    {
        var blob = new Blob().Bytes(Encoding.Unicode.GetBytes("Realtek")).Zeros(40).Bytes(1, 2, 3).ToArray();

        Assert.Equal("\"Realtek\"  (+ 3 bytes of other data)", Summary(null, blob));
    }

    [Fact]
    public void Binary_that_happens_to_be_valid_utf16_is_not_text()
    {
        // 中文字 — valid UTF-16, but not plain text; earlier versions showed blobs like this as CJK garbage.
        var blob = new Blob().Bytes(Encoding.Unicode.GetBytes("中文字")).Zeros(4).ToArray();

        Assert.Null(Summary(null, blob));
    }

    [Fact]
    public void Random_bytes_are_not_decoded() =>
        Assert.Null(Summary(null, Enumerable.Repeat((byte)0xFF, 16).ToArray()));

    [Fact]
    public void Endpoint_id_is_resolved_to_its_name()
    {
        const string id = "{0.0.0.00000000}.{6a2a4d0f-3b6e-4a49-9d3b-6b8a1f7c2e11}";

        Assert.Equal("Speakers", Summary(null, id, i => i == id ? "Speakers" : null));
        Assert.Equal($"prefix {id}  → Speakers", Summary(null, $"prefix {id}", i => i == id ? "Speakers" : null));
        Assert.Null(Summary(null, id));
    }

    [Fact]
    public void Dates_use_an_unambiguous_format() =>
        Assert.Equal("2026-11-25 10:20:30", Summary(null, new DateTime(2026, 11, 25, 10, 20, 30)));

    [Fact]
    public void Plain_values_are_left_alone()
    {
        Assert.Null(Summary(null, 42u));
        Assert.Null(Summary(null, "just text"));
        Assert.Null(Summary(null, null));
    }

    // ---- raw text -------------------------------------------------------------------------------------------------

    [Fact]
    public void Raw_text_of_long_blobs_is_cut_after_64_bytes()
    {
        string raw = PropertyValueFormatter.FormatRaw(new byte[100]);

        Assert.StartsWith("byte[100] ", raw);
        Assert.EndsWith("…", raw);
        Assert.Equal("byte[100] ".Length + 128 + 1, raw.Length);
    }

    [Fact]
    public void Raw_text_of_string_lists_and_null()
    {
        Assert.Equal("a; b", PropertyValueFormatter.FormatRaw(new[] { "a", "b" }));
        Assert.Equal("(null)", PropertyValueFormatter.FormatRaw(null));
        Assert.Equal("byte[2] 0A0B", PropertyValueFormatter.FormatRaw(new byte[] { 10, 11 }));
    }
}
