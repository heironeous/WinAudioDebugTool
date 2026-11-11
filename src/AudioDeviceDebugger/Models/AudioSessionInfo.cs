namespace AudioDeviceDebugger.Models;

/// <summary>Mirrors Core Audio's AudioSessionState. Active = a stream in the session is running (audio flowing);
/// Inactive = the session exists (the app opened the device before and is still running) but nothing is streaming.</summary>
public enum AudioSessionActivity
{
    Inactive,
    Active,
    Expired,
}

/// <summary>One audio session on an endpoint: an app's (or the system sounds') stream group, as Windows' volume
/// mixer sees it. Plain data; <see cref="Peak"/> is only filled for the watched (selected) device.</summary>
public sealed record AudioSessionInfo
{
    /// <summary>Session instance identifier — unique per session, stable while it exists.</summary>
    public required string Key { get; init; }

    public required string DeviceId { get; init; }
    public required int ProcessId { get; init; }

    /// <summary>Best human name: the session's display name, else the executable's file description
    /// (e.g. "Microsoft Teams"), else the process name.</summary>
    public required string AppName { get; init; }

    public string? ProcessName { get; init; }
    public string? ExecutablePath { get; init; }
    public bool ProcessExited { get; init; }
    public required AudioSessionActivity State { get; init; }
    public bool IsSystemSounds { get; init; }
    public bool IsThisApp { get; init; }
    public float Volume { get; init; }
    public bool Muted { get; init; }
}
