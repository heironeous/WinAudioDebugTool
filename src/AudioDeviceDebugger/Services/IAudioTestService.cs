using AudioDeviceDebugger.Models;

namespace AudioDeviceDebugger.Services;

public interface IAudioTestService
{
    /// <summary>Starts playing the generated test music (<see cref="TestMusic"/>) through the given output device. It
    /// ends by itself after the clip (~5.5 s), or early when <see cref="AudioTestSession.Stop"/> is called.</summary>
    AudioTestSession StartTestSound(string outputDeviceId);

    /// <summary>Starts recording from the given input device; when recording ends (by itself after a fixed
    /// duration, or early via <see cref="AudioTestSession.Stop"/>) the captured audio is played back through
    /// <paramref name="outputDeviceId"/> (or the current default output device if null). Calling
    /// <see cref="AudioTestSession.Stop"/> again during playback cuts the playback short.</summary>
    AudioTestSession StartRecordAndPlayback(string inputDeviceId, string? outputDeviceId);

    /// <summary>Plays the test music (mono) on one output channel only — or, given several channels, on each in turn
    /// (a 1.5 s excerpt per channel) — with all other channels silent. <see cref="AudioTestSession.Channel"/> reports
    /// the channel currently sounding.</summary>
    AudioTestSession StartChannelTestSound(string outputDeviceId, IReadOnlyList<int> channels);

    /// <summary>Plays one channel of a recorded mic take — or several, in turn — in mono on every speaker of
    /// <paramref name="outputDeviceId"/> (or the default output if null).</summary>
    AudioTestSession StartChannelPlayback(RecordedTake take, string inputName, IReadOnlyList<int> channels, string? outputDeviceId);

    /// <summary>A/B recording: records the same few seconds from one mic in the Raw, Call (communications) and
    /// Standard processing modes at once, then plays the Raw and Call takes back to back. The takes end up in
    /// <see cref="AudioTestSession.ModeTakes"/>; <see cref="AudioTestSession.PlayingMode"/> tracks the playback.</summary>
    AudioTestSession StartModeComparison(string inputDeviceId, string? outputDeviceId);

    /// <summary>Plays whole recordings (all channels, mixed to the output) one after another.</summary>
    AudioTestSession StartTakePlayback(IReadOnlyList<RecordedTake> takes, string inputName, string? outputDeviceId);
}
