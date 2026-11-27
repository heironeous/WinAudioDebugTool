using System.ComponentModel;
using AudioDeviceDebugger.Models;

namespace AudioDeviceDebugger.ViewModels;

/// <summary>One line of the details panel's USED BY list: an app's session on the selected device, with a live peak.
/// Updated in place (by session key) so the list doesn't flicker on every scan.</summary>
public sealed class SessionRowViewModel : INotifyPropertyChanged
{
    /// <param name="isOutput">Whether the session is on an output (speaker icons) or an input (mic icons).</param>
    /// <param name="clock">For tests; the current UTC time otherwise.</param>
    public SessionRowViewModel(AudioSessionInfo info, bool isOutput = true, Func<DateTime>? clock = null)
    {
        _info = info;
        IsOutput = isOutput;
        _clock = clock ?? (() => DateTime.UtcNow);
        _volumePercent = Math.Round(info.Volume * 100.0);
        _isMuted = info.Muted;
    }

    private AudioSessionInfo _info;
    private readonly Func<DateTime> _clock;
    public AudioSessionInfo Info => _info;

    public string Key => _info.Key;

    public string AppName => _info.IsThisApp ? "This app (Audio Device Debugger)" : _info.AppName;

    public bool IsActive => _info.State == AudioSessionActivity.Active;

    /// <summary>"CiscoCollabHost.exe · 1234" (blank for the system-sounds session).</summary>
    public string ProcessText => _info.IsSystemSounds ? ""
        : _info.ProcessName is null ? $"PID {_info.ProcessId}"
        : $"{_info.ProcessName}.exe · {_info.ProcessId}";

    /// <summary>"Streaming" / "Idle" (+ " · exited") — Windows' Active / Inactive session state in plain words.</summary>
    public string StateText => (_info.State switch
    {
        AudioSessionActivity.Active => "Streaming",
        AudioSessionActivity.Expired => "Closed",
        _ => "Idle",
    }) + (_info.ProcessExited ? " · exited" : "");

    /// <summary>The app's own volume for this session (Windows volume mixer), e.g. "100 %" or "muted".</summary>
    public string VolumeText => _isMuted ? "muted" : $"{Math.Round(_volumePercent)} %";

    // ---- App volume and mute (VOLUME column) -------------------------------------------------------------------------

    public bool IsOutput { get; }

    /// <summary>The volume mixer controls every session except this app's own (its meter and test streams) and the
    /// leftovers of an app that has exited.</summary>
    public bool CanAdjustVolume => !_info.IsThisApp && !_info.ProcessExited;

    // After the user moves the slider or clicks mute, scans are ignored this long: a scan taken just before the change
    // reached Windows would otherwise snap the control back for a moment.
    internal static readonly TimeSpan UserEditHold = TimeSpan.FromSeconds(1.5);
    private DateTime _volumeHeldUntil, _muteHeldUntil;

    private double _volumePercent;

    /// <summary>The app's volume, 0–100. Setting it (from the slider) raises <see cref="VolumeEditedByUser"/>.</summary>
    public double VolumePercent
    {
        get => _volumePercent;
        set
        {
            value = Math.Round(Math.Clamp(value, 0, 100));
            if (_volumePercent == value) return;
            _volumePercent = value;
            _volumeHeldUntil = _clock() + UserEditHold;
            OnPropertyChanged(nameof(VolumePercent));
            OnPropertyChanged(nameof(VolumeText));
            VolumeEditedByUser?.Invoke(this, EventArgs.Empty);
        }
    }

    private bool _isMuted;

    /// <summary>The app's mute. Setting it (from the mute button) raises <see cref="MuteEditedByUser"/>.</summary>
    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            if (_isMuted == value) return;
            _isMuted = value;
            _muteHeldUntil = _clock() + UserEditHold;
            OnPropertyChanged(nameof(IsMuted));
            OnPropertyChanged(nameof(VolumeText));
            MuteEditedByUser?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? VolumeEditedByUser;
    public event EventHandler? MuteEditedByUser;

    /// <summary>Takes volume and mute from a scan without raising the "edited by user" events, except right after
    /// the user changed them here.</summary>
    private void ApplyScannedVolume(AudioSessionInfo info)
    {
        var now = _clock();
        double percent = Math.Round(info.Volume * 100.0);
        if (now >= _volumeHeldUntil && _volumePercent != percent)
        {
            _volumePercent = percent;
        }
        if (now >= _muteHeldUntil && _isMuted != info.Muted)
        {
            _isMuted = info.Muted;
        }
    }

    public string ToolTip
    {
        get
        {
            var lines = new List<string> { AppName };
            if (_info.IsSystemSounds)
            {
                lines.Add("Windows notification and system sounds");
            }
            else
            {
                string process = _info.ProcessName is null ? $"PID {_info.ProcessId}" : $"{_info.ProcessName}.exe, PID {_info.ProcessId}";
                lines.Add(process + (_info.ProcessExited ? " (process has exited)" : ""));
                if (_info.ExecutablePath is not null) lines.Add(_info.ExecutablePath);
            }
            lines.Add(IsActive
                ? "Streaming: the app has a running stream on this device."
                : "Idle: the app opened this device earlier and is still running, but isn't streaming now.");
            lines.Add($"App volume (Windows volume mixer): {VolumeText}");
            if (_info.ProcessName is "chrome" or "msedge" or "firefox" or "brave" or "opera")
            {
                lines.Add("Browsers play and record through one audio process, not per tab.");
            }
            return string.Join("\n", lines);
        }
    }

    // ---- Per-app device (OUTPUT / INPUT column) --------------------------------------------------------------------

    /// <summary>Whether the app's device can be chosen here: not for the system sounds, this app, or an app that has
    /// exited (Windows only answers for running processes that use audio), nor when Windows didn't tell us
    /// the app's current setting.</summary>
    public bool CanRoute => !_info.IsSystemSounds && !_info.IsThisApp && !_info.ProcessExited && _routeKnown;

    private bool _routeKnown;
    private string? _routeEndpointId;
    private string _routeText = "—";
    private string _routeToolTip = "";

    /// <summary>The endpoint the app is set to use instead of the Windows default, or null when it follows the
    /// default.</summary>
    public string? RouteEndpointId => _routeEndpointId;

    /// <summary>"Default", the name of the device the app is set to, or "—" when it can't be set.</summary>
    public string RouteText => _routeText;

    public string RouteToolTip => _routeToolTip;

    /// <summary>Shows the app's per-app device. <paramref name="known"/> is false when Windows didn't say
    /// (<paramref name="reason"/> says why); <paramref name="endpointName"/> names <paramref name="endpointId"/>.</summary>
    public void SetRoute(bool known, string? endpointId, string? endpointName, string direction, string? reason = null)
    {
        _routeKnown = known;
        _routeEndpointId = known ? endpointId : null;
        _routeText = RouteTextFor(_info, known, endpointId, endpointName);
        _routeToolTip = RouteToolTipFor(_info, known, endpointId, endpointName, direction, reason);
        OnPropertyChanged(nameof(CanRoute));
        OnPropertyChanged(nameof(RouteEndpointId));
        OnPropertyChanged(nameof(RouteText));
        OnPropertyChanged(nameof(RouteToolTip));
    }

    internal static string RouteTextFor(AudioSessionInfo info, bool known, string? endpointId, string? endpointName) =>
        info.IsSystemSounds || info.IsThisApp || info.ProcessExited || !known ? "—"
        : endpointId is null ? "Default"
        : endpointName ?? "Unknown device";

    internal static string RouteToolTipFor(AudioSessionInfo info, bool known, string? endpointId, string? endpointName,
        string direction, string? reason)
    {
        if (info.IsSystemSounds) return $"System sounds always use the Windows default {direction}.";
        if (info.IsThisApp) return "This app opens the devices it tests directly.";
        if (info.ProcessExited) return "The app has exited.";
        if (!known) return $"Can't read this app's {direction} setting" + (reason is null ? "." : $": {reason}.");

        string now = endpointId is null
            ? $"Follows the Windows default {direction}."
            : $"Set to \"{endpointName ?? endpointId}\" instead of the Windows default {direction}.";
        return now + $"\nClick to choose the {direction} for this app, like \"App volume and device preferences\" in "
               + "the Windows Sound settings. Windows remembers it per app, also after a restart. Apps that pick a "
               + "device themselves (in their own settings) ignore it.";
    }

    private float _peak;
    public float Peak
    {
        get => _peak;
        set
        {
            if (_peak == value) return;
            _peak = value;
            OnPropertyChanged(nameof(Peak));
            OnPropertyChanged(nameof(MeterLevel));
            OnPropertyChanged(nameof(LevelText));
        }
    }

    public double MeterLevel => LevelScale.ToMeter(_peak);
    public string LevelText => LevelScale.Format(_peak);

    public void Update(AudioSessionInfo info)
    {
        if (info == _info) return;
        _info = info;
        ApplyScannedVolume(info);
        OnPropertyChanged(string.Empty); // everything derived from Info (the route is set again by the caller)
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
