using System.ComponentModel;
using AudioDeviceDebugger.Models;

namespace AudioDeviceDebugger.ViewModels;

/// <summary>Wraps a DeviceInfo snapshot with the mutable bits the UI needs (live level) without mutating
/// the snapshot itself — a fresh DeviceInfo replaces this on every Refresh.</summary>
public sealed class DeviceRowViewModel : INotifyPropertyChanged
{
    public DeviceRowViewModel(DeviceInfo device)
    {
        Device = device;
    }

    public DeviceInfo Device { get; }

    /// <summary>The list section (OUTPUTS / INPUTS) when the list is grouped by direction. Set by MainViewModel.</summary>
    public DeviceListGroup? SectionGroup { get; set; }

    /// <summary>The physical device when the list is grouped by device. Set by MainViewModel.</summary>
    public DeviceListGroup? DeviceGroup { get; set; }

    private bool _showDirection;

    /// <summary>On while the list is grouped by device: the sections no longer say which rows are outputs, so the
    /// state line does ("Output · Active").</summary>
    public bool ShowDirection
    {
        get => _showDirection;
        set
        {
            if (_showDirection == value) return;
            _showDirection = value;
            OnPropertyChanged(nameof(DirectionPrefix));
        }
    }

    public string DirectionPrefix => _showDirection ? (IsOutput ? "Output · " : "Input · ") : "";

    /// <summary>Secondary line under the device name in the list.</summary>
    public string StateText => Device.State switch
    {
        DeviceConnectionState.NotPresent => "Not present",
        var state => state.ToString(),
    };

    public bool IsActive => Device.State == DeviceConnectionState.Active;

    /// <summary>Segoe Fluent Icons glyph for the kind of device (its form factor). Drivers often report a plain
    /// "Speakers" or "Microphone" for a headset, so the icon is only as specific as they are.</summary>
    public string IconGlyph => Device.FormFactor switch
    {
        0 => "\uE968",          // network device
        1 => "\uE7F5",          // speaker
        2 or 7 or 8 => "\uE95F", // plug: line level, digital passthrough, S/PDIF
        3 => "\uE7F6",          // headphones
        4 => "\uE720",          // microphone
        5 => "\uE95B",          // headset
        6 => "\uE717",          // phone handset
        9 => "\uE7F4",          // display (HDMI / DisplayPort)
        _ => IsOutput ? "\uE767" : "\uE720", // not reported or unknown: plain speaker sound / microphone
    };

    /// <summary>The kind of device as Windows reports it, e.g. "Headset".</summary>
    public string IconToolTip => Device.FormFactor is { } form
        ? Services.PropertyValueFormatter.FormFactorName((uint)form)
        : "Form factor not reported";

    private double _currentPeak;

    /// <summary>Latest IAudioMeterInformation reading, linear 0.0–1.0. Updated on the UI thread by MainViewModel.</summary>
    public double CurrentPeak
    {
        get => _currentPeak;
        set
        {
            if (_currentPeak == value) return;
            _currentPeak = value;
            OnPropertyChanged(nameof(CurrentPeak));
            OnPropertyChanged(nameof(MeterLevel));
            OnPropertyChanged(nameof(LevelText));
        }
    }

    /// <summary>Bar fill (0–1) on the dB scale (−60…0 dBFS) — see <see cref="LevelScale"/>.</summary>
    public double MeterLevel => LevelScale.ToMeter(_currentPeak);

    /// <summary>Current level as text, e.g. "−24.3 dBFS" (shown as the level bar's tooltip).</summary>
    public string LevelText => LevelScale.Format(_currentPeak);

    private bool _isTesting;

    /// <summary>True while a Test (test music or record+playback) is running on this device; drives the
    /// play/stop icon on the row's Test button.</summary>
    public bool IsTesting
    {
        get => _isTesting;
        set
        {
            if (_isTesting == value) return;
            _isTesting = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsTesting)));
        }
    }

    private bool _isPlayingBack;

    /// <summary>True while an input test is in its playback phase; the Test button then reads "Playback ■".</summary>
    public bool IsPlayingBack
    {
        get => _isPlayingBack;
        set
        {
            if (_isPlayingBack == value) return;
            _isPlayingBack = value;
            OnPropertyChanged(nameof(IsPlayingBack));
        }
    }

    public bool IsOutput => Device.Direction == AudioDirection.Output;

    private bool _hasVolume;

    /// <summary>True when the endpoint exposes a volume control (i.e. it's active).</summary>
    public bool HasVolume
    {
        get => _hasVolume;
        private set
        {
            if (_hasVolume == value) return;
            _hasVolume = value;
            OnPropertyChanged(nameof(HasVolume));
        }
    }

    private double _volumePercent;

    /// <summary>Master volume, 0–100. Setting it (from the slider) raises <see cref="VolumeEditedByUser"/>.</summary>
    public double VolumePercent
    {
        get => _volumePercent;
        set
        {
            if (_volumePercent == value) return;
            _volumePercent = value;
            OnPropertyChanged(nameof(VolumePercent));
            VolumeEditedByUser?.Invoke(this, EventArgs.Empty);
        }
    }

    private bool _isMuted;

    /// <summary>Endpoint mute. Setting it (from the mute button) raises <see cref="MuteEditedByUser"/>.</summary>
    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            if (_isMuted == value) return;
            _isMuted = value;
            OnPropertyChanged(nameof(IsMuted));
            MuteEditedByUser?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? VolumeEditedByUser;
    public event EventHandler? MuteEditedByUser;

    /// <summary>Updates volume/mute from the device itself (initial read or an external change) without
    /// raising the "edited by user" events, so it doesn't get written straight back.</summary>
    public void ApplyVolumeState(EndpointVolumeState? state)
    {
        HasVolume = state is not null;
        if (state is not { } s) return;

        double percent = Math.Round(s.Volume * 100.0, 1);
        if (_volumePercent != percent)
        {
            _volumePercent = percent;
            OnPropertyChanged(nameof(VolumePercent));
        }
        if (_isMuted != s.Muted)
        {
            _isMuted = s.Muted;
            OnPropertyChanged(nameof(IsMuted));
        }
    }

    private string _usageSuffix = "";

    /// <summary>Appended to the state line when other apps are streaming on this device, e.g.
    /// " · In use by Webex Meetings" (" +1" for more). Empty otherwise.</summary>
    public string UsageSuffix
    {
        get => _usageSuffix;
        private set
        {
            if (_usageSuffix == value) return;
            _usageSuffix = value;
            OnPropertyChanged(nameof(UsageSuffix));
        }
    }

    private string? _usageToolTip;

    /// <summary>Which apps have sessions here (streaming first), plus the call-mode hint for headset mics.</summary>
    public string? UsageToolTip
    {
        get => _usageToolTip;
        private set
        {
            if (_usageToolTip == value) return;
            _usageToolTip = value;
            OnPropertyChanged(nameof(UsageToolTip));
        }
    }

    /// <summary>
    /// Summarises the device's audio sessions. "In use" means another app is streaming right now; this app's own
    /// streams (the selected mic's meter, tests) and the system-sounds session don't count.
    /// </summary>
    public void ApplySessions(IReadOnlyList<AudioSessionInfo> sessions)
    {
        var others = sessions.Where(s => !s.IsThisApp && s.State != AudioSessionActivity.Expired).ToList();
        var streaming = others.Where(s => s.State == AudioSessionActivity.Active && !s.IsSystemSounds)
            .Select(s => s.AppName).Distinct().ToList();

        UsageSuffix = streaming.Count switch
        {
            0 => "",
            1 => $" · In use by {streaming[0]}",
            _ => $" · In use by {streaming[0]} +{streaming.Count - 1}",
        };

        if (others.Count == 0)
        {
            UsageToolTip = null;
            return;
        }

        var lines = new List<string>();
        foreach (var s in others.OrderByDescending(s => s.State == AudioSessionActivity.Active).ThenBy(s => s.AppName))
        {
            string state = s.State == AudioSessionActivity.Active ? "streaming" : "idle";
            string pid = s.IsSystemSounds ? "" : $", PID {s.ProcessId}";
            lines.Add($"{s.AppName} — {state}{pid}");
        }
        if (streaming.Count > 0 && Device.IsHeadsetMic)
        {
            lines.Add("");
            lines.Add(Device.LinkedOutputName is { } output
                ? $"This mic belongs to the same device as \"{output}\". If that is a headset (or its dongle), the " +
                  "open mic usually switches it into call (hands-free) mode: its playback drops to call quality " +
                  "until the mic is released."
                : "This headset mic is open, which usually switches the headset into call (hands-free) mode: " +
                  "its playback drops to call quality until the mic is released.");
        }
        UsageToolTip = string.Join("\n", lines);
    }

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public event PropertyChangedEventHandler? PropertyChanged;
}
