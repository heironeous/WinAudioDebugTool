namespace AudioDeviceDebugger.Models;

public enum AudioEventKind
{
    Started,
    DeviceAdded,
    DeviceRemoved,
    StateChanged,
    DefaultChanged,
    DefaultRequested,
    VolumeChanged,
    MuteChanged,
    TestStarted,
    TestFinished,
    PropertyChanged,
    Error,
}

/// <summary>Groups of event kinds the event panel can show or hide. <see cref="Other"/> (start, errors) is always shown.</summary>
public enum AudioEventCategory
{
    Devices,
    Defaults,
    Volume,
    Tests,
    Properties,
    Other,
}

/// <summary>Who caused the event: Windows / another app / the user elsewhere, or this app itself.</summary>
public enum AudioEventSource
{
    Windows,
    ThisApp,
}

/// <summary>One line of the event timeline. Immutable: coalescing a burst (e.g. a volume drag) replaces the entry
/// with an updated copy (<see cref="LastTimestamp"/>, <see cref="Count"/>, <see cref="Detail"/>).</summary>
public sealed record AudioEvent
{
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>Time of the last notification merged into this entry (equals <see cref="Timestamp"/> if none).</summary>
    public DateTimeOffset LastTimestamp { get; init; }

    public required AudioEventKind Kind { get; init; }
    public required AudioEventSource Source { get; init; }
    public string? DeviceId { get; init; }
    public string? DeviceName { get; init; }
    public AudioDirection? Direction { get; init; }
    public required string Detail { get; init; }

    /// <summary>How many raw notifications this entry stands for (bursts are merged into one line).</summary>
    public int Count { get; init; } = 1;

    /// <summary>Starting value of a coalesced change (e.g. the volume before a drag began).</summary>
    public string? FromValue { get; init; }

    /// <summary>Lowest / highest value reached during a coalesced numeric change (e.g. a volume dip that was
    /// restored), so "100 % → 100 %" can still show that it went down to 90 % in between.</summary>
    public int? Low { get; init; }
    public int? High { get; init; }

    public AudioEventCategory Category => CategoryOf(Kind);

    public string TimeText => Timestamp.ToString("HH:mm:ss.fff");

    public string KindText => Kind switch
    {
        AudioEventKind.Started => "Started",
        AudioEventKind.DeviceAdded => "Added",
        AudioEventKind.DeviceRemoved => "Removed",
        AudioEventKind.StateChanged => "State",
        AudioEventKind.DefaultChanged => "Default",
        AudioEventKind.DefaultRequested => "Set default",
        AudioEventKind.VolumeChanged => "Volume",
        AudioEventKind.MuteChanged => "Mute",
        AudioEventKind.TestStarted => "Test",
        AudioEventKind.TestFinished => "Test",
        AudioEventKind.PropertyChanged => "Property",
        AudioEventKind.Error => "Error",
        _ => Kind.ToString(),
    };

    public string SourceText => Source == AudioEventSource.ThisApp ? "this app" : "Windows";

    public string DeviceText => DeviceName ?? "";

    /// <summary>Full text for the row tooltip: detail, device, who caused it, and how many notifications were merged.</summary>
    public string ToolTipText
    {
        get
        {
            string text = Detail;
            if (DeviceName is not null) text += $"\n{DeviceName}";
            text += Source == AudioEventSource.ThisApp ? "\nCaused by this app" : "\nReported by Windows";
            if (Count > 1)
            {
                text += $"\n{Count} notifications merged, {Timestamp:HH:mm:ss.fff}–{LastTimestamp:HH:mm:ss.fff}";
            }
            return text;
        }
    }

    public static AudioEventCategory CategoryOf(AudioEventKind kind) => kind switch
    {
        AudioEventKind.DeviceAdded or AudioEventKind.DeviceRemoved or AudioEventKind.StateChanged => AudioEventCategory.Devices,
        AudioEventKind.DefaultChanged or AudioEventKind.DefaultRequested => AudioEventCategory.Defaults,
        AudioEventKind.VolumeChanged or AudioEventKind.MuteChanged => AudioEventCategory.Volume,
        AudioEventKind.TestStarted or AudioEventKind.TestFinished => AudioEventCategory.Tests,
        AudioEventKind.PropertyChanged => AudioEventCategory.Properties,
        _ => AudioEventCategory.Other,
    };
}
