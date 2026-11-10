using AudioDeviceDebugger.Interop;
using AudioDeviceDebugger.Models;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace AudioDeviceDebugger.Services;

public sealed class DeviceDetailsService : IDeviceDetailsService, IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();

    public DeviceDetails GetDetails(string deviceId)
    {
        using var device = _enumerator.GetDevice(deviceId);
        using var reader = new EndpointPropertyReader(); // not device.FriendlyName — see EndpointPropertyReader

        return new DeviceDetails
        {
            Id = device.ID,
            FriendlyName = reader.TryGetString(device.ID, PropertyKeys.PKEY_Device_FriendlyName) ?? device.ID,
            Direction = device.DataFlow == DataFlow.Render ? AudioDirection.Output : AudioDirection.Input,
            State = MapState(device.State),
            SharedModeFormat = TryGetSharedModeFormat(device),
            ExclusiveModeFormat = TryGetExclusiveModeFormat(device),
        };
    }

    public IReadOnlyList<PropertyEntry> GetProperties(string deviceId)
    {
        using var device = _enumerator.GetDevice(deviceId);
        return DumpProperties(device);
    }

    private static AudioFormatInfo? TryGetSharedModeFormat(MMDevice device)
    {
        try
        {
            var mix = device.AudioClient.MixFormat;
            return ToFormatInfo(mix, device.DataFlow == DataFlow.Render);
        }
        catch
        {
            // Device isn't active (disabled/unplugged) — AudioClient can't be instantiated.
            return null;
        }
    }

    private static AudioFormatInfo? TryGetExclusiveModeFormat(MMDevice device)
    {
        try
        {
            var mix = device.AudioClient.MixFormat;
            bool supported = device.AudioClient.IsFormatSupported(
                AudioClientShareMode.Exclusive, mix, out WaveFormatExtensible? closestMatch);

            bool isOutput = device.DataFlow == DataFlow.Render;
            if (supported)
            {
                return ToFormatInfo(mix, isOutput);
            }

            return closestMatch is null ? null : ToFormatInfo(closestMatch, isOutput);
        }
        catch
        {
            return null;
        }
    }

    private static AudioFormatInfo ToFormatInfo(WaveFormat format, bool isOutput) => new()
    {
        SampleRateHz = format.SampleRate,
        BitsPerSample = format.BitsPerSample,
        Channels = format.Channels,
        Encoding = format.Encoding.ToString(),
        ChannelLayout = ChannelLayout.Describe(format, isOutput),
    };

    private static IReadOnlyList<PropertyEntry> DumpProperties(MMDevice device)
    {
        var entries = new List<PropertyEntry>();
        try
        {
            var store = device.Properties;
            for (int i = 0; i < store.Count; i++)
            {
                try
                {
                    var prop = store[i];
                    var (formatId, propertyId) = (prop.Key.formatId, prop.Key.propertyId);
                    string raw = PropertyKeyNames.FormatRaw(formatId, propertyId);
                    string? symbol = PropertyKeyNames.TryGetSymbol(formatId, propertyId);
                    entries.Add(new PropertyEntry
                    {
                        Name = symbol is null ? raw : PropertyKeyNames.ToDisplayName(symbol),
                        RawKey = raw,
                        Symbol = symbol,
                        Value = FormatValue(prop.Value),
                    });
                }
                catch
                {
                    // Some properties (notably binary blobs) fail to marshal as a plain object; skip them
                    // rather than losing the whole dump.
                }
            }
        }
        catch
        {
            // Property store unavailable for this device state.
        }

        // Named properties first (alphabetical), then the unrecognised raw keys.
        return entries
            .OrderBy(e => e.IsKnown ? 0 : 1)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string FormatValue(object? value) => value switch
    {
        null => "(null)",
        byte[] bytes when bytes.Length <= 64 => $"byte[{bytes.Length}] {Convert.ToHexString(bytes)}",
        byte[] bytes => $"byte[{bytes.Length}] {Convert.ToHexString(bytes, 0, 64)}…",
        string[] strings => string.Join("; ", strings),
        _ => value.ToString() ?? "(null)",
    };

    private static DeviceConnectionState MapState(DeviceState state) => state switch
    {
        DeviceState.Active => DeviceConnectionState.Active,
        DeviceState.Disabled => DeviceConnectionState.Disabled,
        DeviceState.NotPresent => DeviceConnectionState.NotPresent,
        DeviceState.Unplugged => DeviceConnectionState.Unplugged,
        _ => DeviceConnectionState.NotPresent,
    };

    public void Dispose() => _enumerator.Dispose();
}
