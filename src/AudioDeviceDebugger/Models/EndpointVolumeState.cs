namespace AudioDeviceDebugger.Models;

/// <summary>Snapshot of an endpoint's master volume (0.0–1.0 scalar, as shown in Windows' volume UI) and mute flag.</summary>
public readonly record struct EndpointVolumeState(float Volume, bool Muted);
