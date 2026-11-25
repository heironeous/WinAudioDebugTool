using AudioDeviceDebugger.Models;

namespace AudioDeviceDebugger.Tests;

/// <summary>Builds timeline entries with readable defaults.</summary>
internal static class EventFactory
{
    public static readonly DateTimeOffset T0 = new(2026, 11, 25, 10, 0, 0, TimeSpan.FromHours(1));

    public static AudioEvent Make(
        AudioEventKind kind = AudioEventKind.VolumeChanged,
        double seconds = 0,
        string detail = "detail",
        string? deviceId = "dev-1",
        string? deviceName = "Speakers",
        AudioEventSource source = AudioEventSource.Windows) => new()
    {
        Timestamp = T0.AddSeconds(seconds),
        Kind = kind,
        Source = source,
        DeviceId = deviceId,
        DeviceName = deviceName,
        Direction = deviceId is null ? null : AudioDirection.Output,
        Detail = detail,
    };
}
