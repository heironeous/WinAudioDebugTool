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

    /// <summary>Latest IAudioMeterInformation reading, 0.0–1.0. Updated on the UI thread by MainViewModel.</summary>
    public double CurrentPeak
    {
        get => _currentPeak;
        set
        {
            if (_currentPeak == value) return;
            _currentPeak = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentPeak)));
        }
    }

    private bool _isTesting;

    /// <summary>True while a Test (tone or record+playback) is running on this device; drives the
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

    public event PropertyChangedEventHandler? PropertyChanged;
}
