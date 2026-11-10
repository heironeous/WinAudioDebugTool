using System.ComponentModel;
using AudioDeviceDebugger.Models;

namespace AudioDeviceDebugger.ViewModels;

/// <summary>One channel in the detail panel's channel strip: live peak bar, dB readout, and a per-channel play
/// button (outputs: test music on this channel only; inputs: this channel of the last recording).</summary>
public sealed class ChannelPeakViewModel : INotifyPropertyChanged
{
    public ChannelPeakViewModel(int channelIndex)
    {
        Index = channelIndex;
        _shortName = $"Ch {channelIndex + 1}";
        _longName = $"Channel {channelIndex + 1}";
    }

    public int Index { get; }

    private string _shortName;
    /// <summary>Label under the bar: "FL", "LFE", "Ch 3"…</summary>
    public string ShortName
    {
        get => _shortName;
        set => Set(ref _shortName, value, nameof(ShortName));
    }

    private string _longName;
    /// <summary>Full name for tooltips: "Front Left", "Input channel 3"…</summary>
    public string LongName
    {
        get => _longName;
        set
        {
            if (Set(ref _longName, value, nameof(LongName))) Raise(nameof(LevelText));
        }
    }

    private double _peak;
    /// <summary>Raw linear peak (0–1) as reported by Windows.</summary>
    public double Peak
    {
        get => _peak;
        set
        {
            if (!Set(ref _peak, value, nameof(Peak))) return;
            Raise(nameof(MeterLevel));
            Raise(nameof(LevelText));
            Raise(nameof(DbShort));
        }
    }

    /// <summary>Bar fill (0–1) on the dB scale — see <see cref="LevelScale"/>.</summary>
    public double MeterLevel => LevelScale.ToMeter(_peak);

    public string LevelText => $"{LongName}: {LevelScale.Format(_peak)}";

    /// <summary>Compact readout under the bar, e.g. "−24" (dBFS), or "—" below the meter floor.</summary>
    public string DbShort
    {
        get
        {
            double db = LevelScale.ToDb(_peak);
            return db < LevelScale.FloorDb ? "—" : $"{db:0}";
        }
    }

    private bool _isPlaying;
    /// <summary>True while this channel is the one sounding in a channel test (highlighted in the strip).</summary>
    public bool IsPlaying
    {
        get => _isPlaying;
        set => Set(ref _isPlaying, value, nameof(IsPlaying));
    }

    private bool _canPlay;
    public bool CanPlay
    {
        get => _canPlay;
        set => Set(ref _canPlay, value, nameof(CanPlay));
    }

    private string _playToolTip = "";
    public string PlayToolTip
    {
        get => _playToolTip;
        set => Set(ref _playToolTip, value, nameof(PlayToolTip));
    }

    private bool Set<T>(ref T field, T value, string name)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public event PropertyChangedEventHandler? PropertyChanged;
}
