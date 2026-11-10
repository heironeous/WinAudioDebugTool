using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using AudioDeviceDebugger.Models;

namespace AudioDeviceDebugger.ViewModels;

/// <summary>
/// The event timeline: an in-memory ring buffer (newest first) of device, default, volume and test events, with
/// per-category filters. UI-thread only — callers marshal notifications onto the dispatcher first.
/// </summary>
public sealed class EventLogViewModel : INotifyPropertyChanged
{
    public const int Capacity = 5000;

    // How far back from the newest entry a coalescing target is searched for (bursts are always near the top).
    private const int MergeSearchDepth = 200;

    private readonly Dictionary<string, AudioEvent> _mergeTargets = new();

    public EventLogViewModel()
    {
        View = CollectionViewSource.GetDefaultView(Events);
        View.Filter = item => item is AudioEvent e && IsShown(e);
    }

    /// <summary>All recorded events, newest first.</summary>
    public ObservableCollection<AudioEvent> Events { get; } = new();

    /// <summary>The filtered view the panel binds to.</summary>
    public ICollectionView View { get; }

    private bool _showDevices = true;
    public bool ShowDevices { get => _showDevices; set { if (SetField(ref _showDevices, value)) ApplyFilter(); } }

    private bool _showDefaults = true;
    public bool ShowDefaults { get => _showDefaults; set { if (SetField(ref _showDefaults, value)) ApplyFilter(); } }

    private bool _showVolume = true;
    public bool ShowVolume { get => _showVolume; set { if (SetField(ref _showVolume, value)) ApplyFilter(); } }

    private bool _showTests = true;
    public bool ShowTests { get => _showTests; set { if (SetField(ref _showTests, value)) ApplyFilter(); } }

    private volatile bool _recordPropertyChanges;

    /// <summary>Property-store changes are very chatty, so they're only <em>recorded</em> while this is on (off by
    /// default) — otherwise they would push everything else out of the buffer. Read from notification threads.</summary>
    public bool RecordPropertyChanges
    {
        get => _recordPropertyChanges;
        set
        {
            if (_recordPropertyChanges == value) return;
            _recordPropertyChanges = value;
            OnPropertyChanged();
            ApplyFilter();
        }
    }

    private bool _selectedDeviceOnly;
    public bool SelectedDeviceOnly { get => _selectedDeviceOnly; set { if (SetField(ref _selectedDeviceOnly, value)) ApplyFilter(); } }

    private string? _selectedDeviceId;

    /// <summary>Kept in sync with the device list selection, for <see cref="SelectedDeviceOnly"/>.</summary>
    public string? SelectedDeviceId
    {
        get => _selectedDeviceId;
        set
        {
            if (_selectedDeviceId == value) return;
            _selectedDeviceId = value;
            if (_selectedDeviceOnly) ApplyFilter();
        }
    }

    /// <summary>e.g. "12 of 40" when filtered, "40" otherwise.</summary>
    public string CountText
    {
        get
        {
            int shown = ShownCount;
            return shown == Events.Count ? $"{Events.Count}" : $"{shown} of {Events.Count}";
        }
    }

    private int ShownCount => Events.Count(IsShown);

    /// <summary>The events currently shown, oldest first (for copy / export).</summary>
    public IReadOnlyList<AudioEvent> ShownEventsOldestFirst() => Events.Where(IsShown).Reverse().ToList();

    public void Add(AudioEvent entry)
    {
        if (entry.LastTimestamp == default)
        {
            entry = entry with { LastTimestamp = entry.Timestamp };
        }
        Events.Insert(0, entry);
        while (Events.Count > Capacity)
        {
            Events.RemoveAt(Events.Count - 1);
        }
        OnPropertyChanged(nameof(CountText));
    }

    /// <summary>
    /// Adds <paramref name="entry"/>, or — if the last entry added under <paramref name="key"/> is recent (its
    /// <see cref="AudioEvent.LastTimestamp"/> within <paramref name="window"/>) and still near the top — replaces that
    /// entry with <c>merge(existing, entry)</c>. Collapses bursts such as a volume drag into a single line.
    /// </summary>
    public void AddOrMerge(AudioEvent entry, string key, TimeSpan window, Func<AudioEvent, AudioEvent, AudioEvent> merge)
    {
        if (_mergeTargets.TryGetValue(key, out var existing) && entry.Timestamp - existing.LastTimestamp <= window)
        {
            int index = IndexOfRecent(existing);
            if (index >= 0)
            {
                var merged = merge(existing, entry) with
                {
                    LastTimestamp = entry.Timestamp,
                    Count = existing.Count + entry.Count,
                };
                Events[index] = merged;
                _mergeTargets[key] = merged;
                return;
            }
        }

        if (entry.LastTimestamp == default)
        {
            entry = entry with { LastTimestamp = entry.Timestamp };
        }
        Add(entry);
        _mergeTargets[key] = entry;
    }

    public void Clear()
    {
        Events.Clear();
        _mergeTargets.Clear();
        OnPropertyChanged(nameof(CountText));
    }

    private int IndexOfRecent(AudioEvent target)
    {
        int depth = Math.Min(Events.Count, MergeSearchDepth);
        for (int i = 0; i < depth; i++)
        {
            if (ReferenceEquals(Events[i], target)) return i;
        }
        return -1;
    }

    private bool IsShown(AudioEvent e)
    {
        bool categoryShown = e.Category switch
        {
            AudioEventCategory.Devices => _showDevices,
            AudioEventCategory.Defaults => _showDefaults,
            AudioEventCategory.Volume => _showVolume,
            AudioEventCategory.Tests => _showTests,
            AudioEventCategory.Properties => _recordPropertyChanges,
            _ => true,
        };
        if (!categoryShown) return false;
        // "Selected device only" keeps device-less entries (start, errors) — they're context for everything.
        return !_selectedDeviceOnly || e.DeviceId is null || e.DeviceId == _selectedDeviceId;
    }

    private void ApplyFilter()
    {
        View.Refresh();
        OnPropertyChanged(nameof(CountText));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
