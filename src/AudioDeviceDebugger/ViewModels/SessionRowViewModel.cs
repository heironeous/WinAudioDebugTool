using System.ComponentModel;
using AudioDeviceDebugger.Models;

namespace AudioDeviceDebugger.ViewModels;

/// <summary>One line of the details panel's USED BY list: an app's session on the selected device, with a live peak.
/// Updated in place (by session key) so the list doesn't flicker on every scan.</summary>
public sealed class SessionRowViewModel : INotifyPropertyChanged
{
    public SessionRowViewModel(AudioSessionInfo info) => _info = info;

    private AudioSessionInfo _info;
    public AudioSessionInfo Info => _info;

    public string Key => _info.Key;

    public string AppName => _info.IsThisApp ? "This app (Audio Device Debugger)" : _info.AppName;

    public bool IsActive => _info.State == AudioSessionActivity.Active;

    /// <summary>"Streaming" / "Idle" (+ " · exited") — Windows' Active / Inactive session state in plain words.</summary>
    public string StateText => (_info.State switch
    {
        AudioSessionActivity.Active => "Streaming",
        AudioSessionActivity.Expired => "Closed",
        _ => "Idle",
    }) + (_info.ProcessExited ? " · exited" : "");

    /// <summary>The app's own volume for this session (Windows volume mixer), e.g. "100 %" or "muted".</summary>
    public string VolumeText => _info.Muted ? "muted" : $"{Math.Round(_info.Volume * 100)} %";

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
        OnPropertyChanged(string.Empty); // everything derived from Info
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
