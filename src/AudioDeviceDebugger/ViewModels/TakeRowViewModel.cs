using System.ComponentModel;
using AudioDeviceDebugger.Models;

namespace AudioDeviceDebugger.ViewModels;

/// <summary>One line of the RECORDINGS list for the selected mic: the last recording made in one processing mode
/// (Raw / Call / Standard), with its level and the effects Windows applied. The line the channel strip plays from is
/// highlighted.</summary>
public sealed class TakeRowViewModel : INotifyPropertyChanged
{
    public TakeRowViewModel(CaptureMode mode) => Mode = mode;

    public CaptureMode Mode { get; }

    public string Name => CaptureModeText.Name(Mode);

    public string Description => CaptureModeText.Description(Mode);

    public RecordedTake? Take { get; private set; }

    public bool HasTake => Take is not null;

    /// <summary>Overall RMS level, e.g. "−40.7 dB".</summary>
    public string LevelText => Take is null ? "" : FormatDb(Take.RmsDb);

    /// <summary>"4 ch · 48 kHz · 5.0 s", or why there is no recording.</summary>
    public string DetailText => Take is null
        ? _error is null ? "not recorded yet" : $"failed: {_error}"
        : $"{Take.Channels} ch · {Take.SampleRate / 1000.0:0.#} kHz · {Take.Duration.TotalSeconds:0.0} s";

    public string EffectsText => Take is null ? ""
        : Take.Effects is null ? "effects not checked"
        : ActiveEffects(Take).Count == 0 ? "no effects"
        : string.Join(", ", ActiveEffects(Take));

    public string ToolTip
    {
        get
        {
            var lines = new List<string> { $"{Name}: {CaptureModeText.Description(Mode)}" };
            if (Take is null)
            {
                lines.Add(_error is null
                    ? "Click Record A/B to record this mic in every mode at once."
                    : $"Couldn't record in this mode: {_error}");
                return string.Join("\n", lines);
            }

            lines.Add($"Recorded {Take.RecordedAt:HH:mm:ss}: {DetailText}");
            lines.Add($"Level: {FormatDb(Take.RmsDb)} RMS, peak {FormatDb(Take.PeakDb)}");
            for (int c = 0; c < Take.Channels; c++)
            {
                lines.Add($"  Ch {c + 1}: {FormatDb(Take.ChannelRmsDb[c])} RMS, peak {FormatDb(Take.ChannelPeakDb[c])}");
            }
            if (Take.Effects is null)
            {
                lines.Add("Effects: not checked (the plain mic Test doesn't query them — use Record A/B)");
            }
            else if (Take.Effects.Count == 0)
            {
                lines.Add("Effects: none reported by Windows");
            }
            else
            {
                lines.Add("Effects reported by Windows:");
                lines.AddRange(Take.Effects.Select(e => $"  {e.Name}{(e.IsOn ? "" : " (off)")}"));
            }
            if (Take.Glitches > 0)
            {
                lines.Add($"Glitches: {Take.Glitches} (audio lost while recording)");
            }
            lines.Add(IsStripSource
                ? "The channel strip plays this recording's channels."
                : "Click the line to play its channels from the channel strip.");
            return string.Join("\n", lines);
        }
    }

    private bool _isPlaying;
    public bool IsPlaying => _isPlaying;

    private bool _isStripSource;
    /// <summary>The channel strip's per-channel buttons play this recording.</summary>
    public bool IsStripSource => _isStripSource;

    private bool _canPlay;
    public bool CanPlay => _canPlay;

    private string _playToolTip = "";
    public string PlayToolTip => _playToolTip;

    private string? _error;

    public void Update(RecordedTake? take, string? error, bool isStripSource, bool isPlaying, bool canPlay, string playToolTip)
    {
        Take = take;
        _error = take is null ? error : null;
        _isStripSource = isStripSource && take is not null;
        _isPlaying = isPlaying;
        _canPlay = canPlay;
        _playToolTip = playToolTip;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }

    private static List<string> ActiveEffects(RecordedTake take) =>
        take.Effects!.Where(e => e.IsOn).Select(e => e.Name).ToList();

    private static string FormatDb(double db) =>
        double.IsNegativeInfinity(db) ? "−∞ dB" : $"{db:0.0} dB".Replace('-', '−');

    public event PropertyChangedEventHandler? PropertyChanged;
}
