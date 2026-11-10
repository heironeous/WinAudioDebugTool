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

        return new DeviceDetails
        {
            Id = device.ID,
            FriendlyName = SafeGet(() => device.FriendlyName, device.ID),
            Direction = device.DataFlow == DataFlow.Render ? AudioDirection.Output : AudioDirection.Input,
            State = MapState(device.State),
            SharedModeFormat = TryGetSharedModeFormat(device),
            ExclusiveModeFormat = TryGetExclusiveModeFormat(device),
            Properties = DumpProperties(device),
        };
    }

    private static AudioFormatInfo? TryGetSharedModeFormat(MMDevice device)
    {
        try
        {
            var mix = device.AudioClient.MixFormat;
            return ToFormatInfo(mix);
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
                AudioClientShareMode.Exclusive, mix, out WaveFormat? closestMatch);

            if (supported)
            {
                return ToFormatInfo(mix);
            }

            return closestMatch is null ? null : ToFormatInfo(closestMatch);
        }
        catch
        {
            return null;
        }
    }

    private static AudioFormatInfo ToFormatInfo(WaveFormat format) => new()
    {
        SampleRateHz = format.SampleRate,
        BitsPerSample = format.BitsPerSample,
        Channels = format.Channels,
        Encoding = format.Encoding.ToString(),
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
                    entries.Add(new PropertyEntry
                    {
                        Key = prop.Key.ToString() ?? $"property[{i}]",
                        Value = prop.Value?.ToString() ?? "(null)",
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

        return entries;
    }

    private static string SafeGet(Func<string> getter, string fallback)
    {
        try
        {
            return getter();
        }
        catch
        {
            return fallback;
        }
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
