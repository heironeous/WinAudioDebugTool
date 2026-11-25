using AudioDeviceDebugger.Models;
using AudioDeviceDebugger.ViewModels;
using static AudioDeviceDebugger.Tests.EventFactory;

namespace AudioDeviceDebugger.Tests;

public class EventLogViewModelTests
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(1);

    private static AudioEvent KeepFirstDetail(AudioEvent existing, AudioEvent next) =>
        existing with { Detail = existing.Detail + " → " + next.Detail };

    [Fact]
    public void Add_puts_the_newest_entry_first_and_fills_LastTimestamp()
    {
        var log = new EventLogViewModel();
        log.Add(Make(seconds: 0, detail: "first"));
        log.Add(Make(seconds: 1, detail: "second"));

        Assert.Equal(new[] { "second", "first" }, log.Events.Select(e => e.Detail));
        Assert.All(log.Events, e => Assert.Equal(e.Timestamp, e.LastTimestamp));
    }

    [Fact]
    public void A_burst_within_the_window_becomes_one_entry()
    {
        var log = new EventLogViewModel();
        log.AddOrMerge(Make(seconds: 0, detail: "50"), "vol:dev-1", Window, KeepFirstDetail);
        log.AddOrMerge(Make(seconds: 0.5, detail: "55"), "vol:dev-1", Window, KeepFirstDetail);
        log.AddOrMerge(Make(seconds: 1.2, detail: "60"), "vol:dev-1", Window, KeepFirstDetail); // 0.7 s after the last

        var entry = Assert.Single(log.Events);
        Assert.Equal("50 → 55 → 60", entry.Detail);
        Assert.Equal(3, entry.Count);
        Assert.Equal(T0, entry.Timestamp);
        Assert.Equal(T0.AddSeconds(1.2), entry.LastTimestamp);
        Assert.Same(entry, Assert.Single(log.TakeUnsaved(null)));
    }

    [Fact]
    public void A_pause_longer_than_the_window_starts_a_new_entry()
    {
        var log = new EventLogViewModel();
        log.AddOrMerge(Make(seconds: 0), "vol:dev-1", Window, KeepFirstDetail);
        log.AddOrMerge(Make(seconds: 2), "vol:dev-1", Window, KeepFirstDetail);

        Assert.Equal(2, log.Events.Count);
    }

    [Fact]
    public void Different_keys_are_never_merged()
    {
        var log = new EventLogViewModel();
        log.AddOrMerge(Make(seconds: 0), "vol:dev-1", Window, KeepFirstDetail);
        log.AddOrMerge(Make(seconds: 0.1), "vol:dev-2", Window, KeepFirstDetail);

        Assert.Equal(2, log.Events.Count);
    }

    [Fact]
    public void The_buffer_keeps_the_newest_entries()
    {
        var log = new EventLogViewModel();
        for (int i = 0; i <= EventLogViewModel.Capacity; i++)
        {
            log.Add(Make(seconds: i, detail: i.ToString()));
        }

        Assert.Equal(EventLogViewModel.Capacity, log.Events.Count);
        Assert.Equal(EventLogViewModel.Capacity.ToString(), log.Events[0].Detail);
        Assert.Equal("1", log.Events[^1].Detail);
    }

    [Fact]
    public void Category_filters_hide_entries_and_update_the_count()
    {
        var log = new EventLogViewModel();
        log.Add(Make(AudioEventKind.VolumeChanged));
        log.Add(Make(AudioEventKind.DeviceAdded));
        Assert.Equal("2", log.CountText);

        log.ShowVolume = false;

        Assert.Equal("1 of 2", log.CountText);
        Assert.Equal(AudioEventKind.DeviceAdded, Assert.Single(log.ShownEventsOldestFirst()).Kind);
        Assert.Single(log.View.Cast<AudioEvent>());
    }

    [Fact]
    public void Property_changes_show_only_while_they_are_recorded()
    {
        var log = new EventLogViewModel();
        log.Add(Make(AudioEventKind.PropertyChanged));
        Assert.Empty(log.ShownEventsOldestFirst());

        log.RecordPropertyChanges = true;

        Assert.Single(log.ShownEventsOldestFirst());
    }

    [Fact]
    public void Selected_device_only_keeps_entries_without_a_device()
    {
        var log = new EventLogViewModel();
        log.Add(Make(AudioEventKind.Started, deviceId: null, deviceName: null));
        log.Add(Make(deviceId: "dev-1"));
        log.Add(Make(deviceId: "dev-2"));

        log.SelectedDeviceId = "dev-2";
        log.SelectedDeviceOnly = true;

        Assert.Equal(new string?[] { null, "dev-2" }, log.ShownEventsOldestFirst().Select(e => e.DeviceId));
    }

    [Fact]
    public void ShownEventsOldestFirst_reverses_the_panel_order()
    {
        var log = new EventLogViewModel();
        log.Add(Make(seconds: 0, detail: "a"));
        log.Add(Make(seconds: 1, detail: "b"));

        Assert.Equal(new[] { "a", "b" }, log.ShownEventsOldestFirst().Select(e => e.Detail));
    }

    [Fact]
    public void TakeUnsaved_stops_at_the_first_entry_that_can_still_change()
    {
        var log = new EventLogViewModel();
        log.Add(Make(seconds: 0, detail: "settled"));
        log.Add(Make(seconds: 5, detail: "recent"));
        log.Add(Make(seconds: 1, detail: "later but older")); // must wait: the file stays in order

        var taken = log.TakeUnsaved(T0.AddSeconds(2));

        Assert.Equal(new[] { "settled" }, taken.Select(e => e.Detail));
        Assert.Equal(new[] { "recent", "later but older" }, log.TakeUnsaved(null).Select(e => e.Detail));
        Assert.Empty(log.TakeUnsaved(null));
    }

    [Fact]
    public void A_merge_into_an_entry_already_written_is_written_again()
    {
        var log = new EventLogViewModel();
        log.AddOrMerge(Make(seconds: 0, detail: "50"), "vol:dev-1", Window, KeepFirstDetail);
        log.TakeUnsaved(null);

        log.AddOrMerge(Make(seconds: 0.5, detail: "60"), "vol:dev-1", Window, KeepFirstDetail);

        Assert.Equal("50 → 60", Assert.Single(log.TakeUnsaved(null)).Detail);
    }

    [Fact]
    public void Clear_empties_the_panel_but_not_the_unsaved_entries()
    {
        var log = new EventLogViewModel();
        log.Add(Make());

        log.Clear();

        Assert.Empty(log.Events);
        Assert.Equal("0", log.CountText);
        Assert.Single(log.TakeUnsaved(null)); // the file is the full record
    }
}
