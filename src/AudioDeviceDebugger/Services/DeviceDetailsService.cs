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
            RawProcessingSupported = device.DataFlow == DataFlow.Capture
                ? reader.TryGetBool(device.ID, AudioPropertyKeys.RawProcessingSupported)
                : null,
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

        // Values that contain an endpoint ID are shown with that endpoint's name (looked up once per ID).
        using var reader = new EndpointPropertyReader();
        var endpointNames = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        string? EndpointName(string endpointId)
        {
            if (!endpointNames.TryGetValue(endpointId, out var name))
            {
                endpointNames[endpointId] = name = reader.TryGetString(endpointId, PropertyKeys.PKEY_Device_FriendlyName);
            }
            return name;
        }

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
                    var undocumented = symbol is null ? PropertyKeyNames.TryGetUndocumented(formatId, propertyId) : null;
                    object? value = prop.Value;
                    string rawValue = PropertyValueFormatter.FormatRaw(value);
                    var decoded = PropertyValueFormatter.Decode(symbol, value, EndpointName);
                    entries.Add(new PropertyEntry
                    {
                        Name = symbol is not null ? PropertyKeyNames.ToDisplayName(symbol)
                            : undocumented?.DisplayName ?? raw,
                        RawKey = raw,
                        Symbol = symbol,
                        Note = undocumented?.Note,
                        Value = decoded?.Summary ?? rawValue,
                        RawValue = rawValue,
                        ValueDetail = decoded?.Detail,
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
