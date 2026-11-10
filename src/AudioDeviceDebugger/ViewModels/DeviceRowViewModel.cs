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

    public string DirectionText => Device.Direction == AudioDirection.Output ? "Output" : "Input";

    public string StateText => Device.State.ToString();

    public string DefaultBadge => (Device.IsDefaultDevice, Device.IsDefaultCommunicationsDevice) switch
    {
        (true, true) => "Default + Comms",
        (true, false) => "Default",
        (false, true) => "Comms",
        (false, false) => "",
    };

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

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public event PropertyChangedEventHandler? PropertyChanged;
}
