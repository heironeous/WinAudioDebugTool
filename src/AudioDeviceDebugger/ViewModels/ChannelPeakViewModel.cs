using System.ComponentModel;

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
    public double Peak
    {
        get => _peak;
        set
        {
            if (_peak == value) return;
            _peak = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Peak)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
