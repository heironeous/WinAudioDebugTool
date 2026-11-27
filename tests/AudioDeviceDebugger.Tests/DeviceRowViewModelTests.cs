using AudioDeviceDebugger.Models;
using AudioDeviceDebugger.ViewModels;

namespace AudioDeviceDebugger.Tests;

public class DeviceRowViewModelTests
{
    private static DeviceInfo Device(
        AudioDirection direction = AudioDirection.Input,
        DeviceConnectionState state = DeviceConnectionState.Active,
        int? formFactor = 4,
        string? linkedOutput = null) => new()
    {
        Id = "dev-1",
        FriendlyName = "Microphone (Jabra Link 390)",
        Direction = direction,
        State = state,
        FormFactor = formFactor,
        LinkedOutputName = linkedOutput,
    };

    private static AudioSessionInfo Session(string app, AudioSessionActivity state = AudioSessionActivity.Active,
        bool thisApp = false, bool systemSounds = false, int pid = 100) => new()
    {
        Key = app + pid,
        DeviceId = "dev-1",
        ProcessId = pid,
        AppName = app,
        State = state,
        IsThisApp = thisApp,
        IsSystemSounds = systemSounds,
    };

    [Fact]
    public void Nobody_else_streaming_means_not_in_use()
    {
        var row = new DeviceRowViewModel(Device());

        row.ApplySessions(new[] { Session("Audio Device Debugger", thisApp: true), Session("Teams", AudioSessionActivity.Expired) });

        Assert.Equal("", row.UsageSuffix);
        Assert.Null(row.UsageToolTip);
    }

    [Fact]
    public void Streaming_apps_are_named_and_counted()
    {
        var row = new DeviceRowViewModel(Device(formFactor: 1, direction: AudioDirection.Output));

        row.ApplySessions(new[]
        {
            Session("Webex", pid: 1), Session("Spotify", pid: 2), Session("Webex", pid: 3),
            Session("Zoom", AudioSessionActivity.Inactive, pid: 4),
            Session("System Sounds", systemSounds: true, pid: 0),
        });

        Assert.Equal(" · In use by Webex +1", row.UsageSuffix);
        Assert.Equal("Spotify — streaming, PID 2\nSystem Sounds — streaming\nWebex — streaming, PID 1\n"
            + "Webex — streaming, PID 3\nZoom — idle, PID 4", row.UsageToolTip);
    }

    [Fact]
    public void An_open_headset_mic_gets_the_call_mode_hint()
    {
        var row = new DeviceRowViewModel(Device(linkedOutput: "Headphones (Jabra Link 390)"));

        row.ApplySessions(new[] { Session("Teams") });

        Assert.Contains("same device as \"Headphones (Jabra Link 390)\"", row.UsageToolTip);
    }

    [Fact]
    public void A_plain_mic_gets_no_call_mode_hint()
    {
        var row = new DeviceRowViewModel(Device());

        row.ApplySessions(new[] { Session("Teams") });

        Assert.DoesNotContain("call", row.UsageToolTip);
    }

    [Fact]
    public void Volume_read_from_the_device_is_not_reported_as_a_user_edit()
    {
        var row = new DeviceRowViewModel(Device());
        int edits = 0;
        row.VolumeEditedByUser += (_, _) => edits++;
        row.MuteEditedByUser += (_, _) => edits++;

        row.ApplyVolumeState(new EndpointVolumeState(0.4567f, true));

        Assert.Equal(0, edits);
        Assert.Equal(45.7, row.VolumePercent);
        Assert.True(row.IsMuted);
        Assert.True(row.HasVolume);

        row.VolumePercent = 50;
        row.IsMuted = false;
        Assert.Equal(2, edits);
    }

    [Fact]
    public void No_volume_state_means_no_volume_control()
    {
        var row = new DeviceRowViewModel(Device());
        row.ApplyVolumeState(new EndpointVolumeState(1, false));

        row.ApplyVolumeState(null);

        Assert.False(row.HasVolume);
    }

    [Theory]
    [InlineData(DeviceConnectionState.NotPresent, "Not present")]
    [InlineData(DeviceConnectionState.Unplugged, "Unplugged")]
    public void State_text(DeviceConnectionState state, string expected) =>
        Assert.Equal(expected, new DeviceRowViewModel(Device(state: state)).StateText);

    [Theory]
    [InlineData(AudioDirection.Output, null, "")]
    [InlineData(AudioDirection.Input, null, "")]
    [InlineData(AudioDirection.Output, 5, "")]
    [InlineData(AudioDirection.Output, 9, "")]
    public void Icon_follows_the_form_factor(AudioDirection direction, int? formFactor, string glyph) =>
        Assert.Equal(glyph, new DeviceRowViewModel(Device(direction, formFactor: formFactor)).IconGlyph);

    [Theory]
    [InlineData(DeviceConnectionState.Active, false, DeviceRowState.Normal)]
    [InlineData(DeviceConnectionState.Active, true, DeviceRowState.Muted)]
    [InlineData(DeviceConnectionState.Disabled, false, DeviceRowState.Disabled)]
    [InlineData(DeviceConnectionState.Unplugged, false, DeviceRowState.Unplugged)]
    [InlineData(DeviceConnectionState.NotPresent, false, DeviceRowState.NotPresent)]
    public void Row_state_follows_the_device_state_and_mute(DeviceConnectionState state, bool muted, DeviceRowState expected)
    {
        var row = new DeviceRowViewModel(Device(state: state));
        if (state == DeviceConnectionState.Active) row.ApplyVolumeState(new EndpointVolumeState(0.5f, muted));

        Assert.Equal(expected, row.RowState);
    }

    [Fact]
    public void Muting_changes_the_row_state()
    {
        var row = new DeviceRowViewModel(Device());
        row.ApplyVolumeState(new EndpointVolumeState(0.5f, false));
        var changed = new List<string?>();
        row.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        row.IsMuted = true;

        Assert.Equal(DeviceRowState.Muted, row.RowState);
        Assert.Contains(nameof(DeviceRowViewModel.RowState), changed);
    }

    [Fact]
    public void Level_text_and_meter_follow_the_peak()
    {
        var row = new DeviceRowViewModel(Device());

        row.CurrentPeak = 0.1;

        Assert.Equal("-20.0 dBFS", row.LevelText);
        Assert.Equal(40 / 60.0, row.MeterLevel, precision: 9);
    }
}
