using AudioDeviceDebugger.Models;

namespace AudioDeviceDebugger.Services;

/// <summary>
/// Watches which apps have audio sessions on each endpoint, and sets an app's volume or mute when the user asks
/// (<see cref="SetSessionVolumeAsync"/>; nothing is changed on its own). Events are raised on the service's own
/// thread; subscribers marshal to the UI thread.
/// </summary>
public interface IAudioSessionService : IDisposable
{
    /// <summary>Raised after a scan whenever any device's session list changed (apps, states, volumes — not
    /// peaks). The first scan is always reported. Contains every watched device, including ones with no sessions.</summary>
    event EventHandler<AudioSessionSnapshot>? SessionsUpdated;

    /// <summary>~10 times a second: the current peak of each session on the device set by
    /// <see cref="SetPeakDevice"/>.</summary>
    event EventHandler<AudioSessionPeaks>? PeaksUpdated;

    /// <summary>The endpoints to watch (the active ones — only they can have sessions).</summary>
    void SetDevices(IReadOnlyCollection<string> deviceIds);

    /// <summary>The endpoint whose session peaks are polled for the details panel (the selected device), or null.</summary>
    void SetPeakDevice(string? deviceId);

    /// <summary>Sets one session's volume (0–1) and/or mute, like the Windows volume mixer — the app's own level, not
    /// the device's. Runs on the service's thread (rapid slider changes are merged); completes with null once done, or
    /// with the reason it failed.</summary>
    Task<string?> SetSessionVolumeAsync(string deviceId, string sessionKey, float? volume, bool? mute);
}

public sealed record AudioSessionSnapshot(
    DateTimeOffset Timestamp,
    IReadOnlyDictionary<string, IReadOnlyList<AudioSessionInfo>> ByDevice);

public sealed record AudioSessionPeaks(string DeviceId, IReadOnlyDictionary<string, float> PeakByKey);
