using AudioDeviceDebugger.Models;
using AudioDeviceDebugger.ViewModels;

namespace AudioDeviceDebugger.Tests;

public class SessionVolumeTests
{
    private static AudioSessionInfo Session(float volume = 1f, bool muted = false, bool thisApp = false,
        bool exited = false, bool systemSounds = false) => new()
    {
        Key = "k",
        DeviceId = "dev",
        ProcessId = 42,
        AppName = "Spotify",
        State = AudioSessionActivity.Active,
        Volume = volume,
        Muted = muted,
        IsThisApp = thisApp,
        ProcessExited = exited,
        IsSystemSounds = systemSounds,
    };

    private DateTime _now = new(2026, 11, 27, 12, 0, 0, DateTimeKind.Utc);

    private SessionRowViewModel Row(AudioSessionInfo info) => new(info, isOutput: true, clock: () => _now);

    [Fact]
    public void Starts_from_the_scanned_volume_and_mute()
    {
        var row = Row(Session(volume: 0.8f, muted: true));

        Assert.Equal(80, row.VolumePercent);
        Assert.True(row.IsMuted);
        Assert.Equal("muted", row.VolumeText);
    }

    [Fact]
    public void A_user_change_is_reported_but_a_scan_is_not()
    {
        var row = Row(Session());
        int volumeEdits = 0, muteEdits = 0;
        row.VolumeEditedByUser += (_, _) => volumeEdits++;
        row.MuteEditedByUser += (_, _) => muteEdits++;

        row.Update(Session(volume: 0.5f, muted: true)); // changed elsewhere (volume mixer)
        Assert.Equal(50, row.VolumePercent);
        Assert.True(row.IsMuted);
        Assert.Equal(0, volumeEdits);
        Assert.Equal(0, muteEdits);

        row.VolumePercent = 30;
        row.IsMuted = false;
        Assert.Equal(1, volumeEdits);
        Assert.Equal(1, muteEdits);
        Assert.Equal("30 %", row.VolumeText);
    }

    [Fact]
    public void A_scan_right_after_a_user_change_does_not_snap_the_slider_back()
    {
        var row = Row(Session(volume: 1f));
        row.VolumePercent = 40;

        _now += TimeSpan.FromSeconds(0.5);
        row.Update(Session(volume: 0.99f)); // scanned before the change reached Windows
        Assert.Equal(40, row.VolumePercent);

        _now += SessionRowViewModel.UserEditHold;
        row.Update(Session(volume: 0.4f));
        Assert.Equal(40, row.VolumePercent);
        row.Update(Session(volume: 0.7f)); // a later change elsewhere is taken
        Assert.Equal(70, row.VolumePercent);
    }

    [Fact]
    public void Slider_values_are_whole_percent_within_range()
    {
        var row = Row(Session());
        row.VolumePercent = 33.4;
        Assert.Equal(33, row.VolumePercent);
        row.VolumePercent = 140;
        Assert.Equal(100, row.VolumePercent);
    }

    [Fact]
    public void This_apps_own_sessions_and_exited_apps_are_not_adjustable()
    {
        Assert.True(Row(Session()).CanAdjustVolume);
        Assert.True(Row(Session(systemSounds: true)).CanAdjustVolume);
        Assert.False(Row(Session(thisApp: true)).CanAdjustVolume);
        Assert.False(Row(Session(exited: true)).CanAdjustVolume);
    }
}
