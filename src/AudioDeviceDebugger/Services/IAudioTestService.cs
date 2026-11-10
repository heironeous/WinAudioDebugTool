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
}
