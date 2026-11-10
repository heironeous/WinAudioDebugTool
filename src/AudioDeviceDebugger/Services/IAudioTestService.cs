using AudioDeviceDebugger.Models;

namespace AudioDeviceDebugger.Services;

public interface IAudioTestService
{
    /// <summary>Plays a short generated tone through the given output device.</summary>
    Task<TestOutcome> PlayTestToneAsync(string outputDeviceId, CancellationToken cancellationToken = default);

    /// <summary>Records a few seconds from the given input device, then plays the recording back through
    /// <paramref name="outputDeviceId"/> (or the current default output device if null) so the user can
    /// hear what the microphone actually captured.</summary>
    Task<TestOutcome> RecordAndPlaybackAsync(string inputDeviceId, string? outputDeviceId, CancellationToken cancellationToken = default);
}
