using AudioDeviceDebugger.Models;
using static AudioDeviceDebugger.Tests.EventFactory;

namespace AudioDeviceDebugger.Tests;

public class AudioEventTests
{
    [Theory]
    [InlineData(AudioEventKind.DeviceAdded, AudioEventCategory.Devices)]
    [InlineData(AudioEventKind.StateChanged, AudioEventCategory.Devices)]
    [InlineData(AudioEventKind.DefaultRequested, AudioEventCategory.Defaults)]
    [InlineData(AudioEventKind.ConnectionRequested, AudioEventCategory.Devices)]
    [InlineData(AudioEventKind.EnableRequested, AudioEventCategory.Devices)]
    [InlineData(AudioEventKind.MuteChanged, AudioEventCategory.Volume)]
    [InlineData(AudioEventKind.TestFinished, AudioEventCategory.Tests)]
    [InlineData(AudioEventKind.SessionStarted, AudioEventCategory.Apps)]
    [InlineData(AudioEventKind.PropertyChanged, AudioEventCategory.Properties)]
    [InlineData(AudioEventKind.Started, AudioEventCategory.Other)]
    [InlineData(AudioEventKind.Error, AudioEventCategory.Other)]
    public void Kinds_belong_to_their_filter_category(AudioEventKind kind, AudioEventCategory category) =>
        Assert.Equal(category, AudioEvent.CategoryOf(kind));

    [Fact]
    public void Every_kind_has_a_label() =>
        Assert.All(Enum.GetValues<AudioEventKind>(), kind =>
            Assert.False(string.IsNullOrWhiteSpace(Make(kind).KindText)));

    [Fact]
    public void Tooltip_names_the_device_the_source_and_merged_notifications()
    {
        var e = Make(detail: "50 % → 60 %") with { Count = 3, LastTimestamp = T0.AddSeconds(2) };

        Assert.Equal("50 % → 60 %\nSpeakers\nReported by Windows\n3 notifications merged, 10:00:00.000–10:00:02.000",
            e.ToolTipText);
    }

    [Fact]
    public void Tooltip_of_a_single_event_caused_by_this_app() =>
        Assert.Equal("Started\nCaused by this app",
            Make(AudioEventKind.Started, detail: "Started", deviceId: null, deviceName: null,
                source: AudioEventSource.ThisApp).ToolTipText);
}
