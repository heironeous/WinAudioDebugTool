using System.ComponentModel;
using AudioDeviceDebugger.Models;

namespace AudioDeviceDebugger.ViewModels;

/// <summary>One channel's live peak bar in the detail panel's per-channel meter row.</summary>
public sealed class ChannelPeakViewModel : INotifyPropertyChanged
{
    public ChannelPeakViewModel(int channelIndex)
    {
        ChannelLabel = $"Ch {channelIndex + 1}";
    }

    public string ChannelLabel { get; }

    private double _peak;

    /// <summary>Raw linear peak (0–1) as reported by Windows.</summary>
    public double Peak
    {
        get => _peak;
        set
        {
            if (_peak == value) return;
            _peak = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Peak)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MeterLevel)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LevelText)));
        }
    }

    /// <summary>Bar fill (0–1) on the dB scale — see <see cref="LevelScale"/>.</summary>
    public double MeterLevel => LevelScale.ToMeter(_peak);

    public string LevelText => $"{ChannelLabel}: {LevelScale.Format(_peak)}";

    public event PropertyChangedEventHandler? PropertyChanged;
}
