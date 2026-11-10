using System.IO;
using AudioDeviceDebugger.Interop;
using AudioDeviceDebugger.Models;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace AudioDeviceDebugger.Services;

public sealed class AudioTestService : IAudioTestService
{
    private static readonly TimeSpan ToneDuration = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RecordDuration = TimeSpan.FromSeconds(5);

    public AudioTestSession StartTestTone(string outputDeviceId) =>
        new(AudioTestPhase.PlayingTone, session => PlayTestTone(outputDeviceId, session));

    public AudioTestSession StartRecordAndPlayback(string inputDeviceId, string? outputDeviceId) =>
        new(AudioTestPhase.Recording, session => RecordAndPlayback(inputDeviceId, outputDeviceId, session));

    private static TestOutcome PlayTestTone(string outputDeviceId, AudioTestSession session)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDevice(outputDeviceId);
            string deviceName = SafeName(device);

            // Generate the tone directly in the device's shared-mode mix format: shared-mode WASAPI won't open a
            // stream whose channel count differs from the device's (e.g. stereo on a 5.1 or mono endpoint).
            var mix = GetMixFormat(device);
            using var output = new WasapiOut(device, AudioClientShareMode.Shared, true, 100);
            var tone = new SignalGenerator(mix.SampleRate, mix.Channels)
            {
                Type = SignalGeneratorType.Sin,
                Frequency = 440,
                Gain = 0.25,
            };

            output.Init(tone);
            var started = DateTime.UtcNow;
            output.Play();
            bool stopped = session.CurrentStopToken.WaitHandle.WaitOne(ToneDuration);
            output.Stop();

            return stopped
                ? TestOutcome.Ok($"Stopped the 440 Hz test tone on \"{deviceName}\" after {(DateTime.UtcNow - started).TotalSeconds:0.0}s.")
                : TestOutcome.Ok($"Played a 440 Hz test tone through \"{deviceName}\" for {ToneDuration.TotalSeconds:0}s.");
        }
        catch (Exception ex)
        {
            return TestOutcome.Fail($"Failed to play test tone: {ex.Message}");
        }
    }

    private static TestOutcome RecordAndPlayback(string inputDeviceId, string? outputDeviceId, AudioTestSession session)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var inputDevice = enumerator.GetDevice(inputDeviceId);
            string inputName = SafeName(inputDevice);

            // Phase 1: record until the fixed duration elapses or the user hits Stop.
            using var capture = new WasapiCapture(inputDevice);
            var recorded = new MemoryStream();
            capture.DataAvailable += (_, e) => recorded.Write(e.Buffer, 0, e.BytesRecorded);

            using var captureStopped = new ManualResetEventSlim(false);
            capture.RecordingStopped += (_, _) => captureStopped.Set();

            capture.StartRecording();
            bool recordingStoppedEarly = session.CurrentStopToken.WaitHandle.WaitOne(RecordDuration);
            capture.StopRecording();
            captureStopped.Wait(TimeSpan.FromSeconds(2));

            if (recorded.Length == 0)
            {
                return TestOutcome.Fail($"No audio captured from \"{inputName}\" — check it's active and not muted.");
            }

            var recordedLength = TimeSpan.FromSeconds((double)recorded.Length / capture.WaveFormat.AverageBytesPerSecond);
            recorded.Position = 0;

            // Phase 2: play back what was captured — even if recording was stopped early.
            var playbackToken = session.BeginPhase(AudioTestPhase.PlayingBack);

            using var outputDevice = outputDeviceId is null
                ? enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)
                : enumerator.GetDevice(outputDeviceId);
            string outputName = SafeName(outputDevice);

            using var playback = new WasapiOut(outputDevice, AudioClientShareMode.Shared, true, 100);
            var rawStream = new RawSourceWaveStream(recorded, capture.WaveFormat);
            playback.Init(ConvertToMixFormat(rawStream.ToSampleProvider(), GetMixFormat(outputDevice)));

            using var playbackStopped = new ManualResetEventSlim(false);
            playback.PlaybackStopped += (_, _) => playbackStopped.Set();

            playback.Play();
            int signalled = WaitHandle.WaitAny(
                new[] { playbackStopped.WaitHandle, playbackToken.WaitHandle },
                recordedLength + TimeSpan.FromSeconds(1));
            bool playbackStoppedEarly = signalled == 1;
            playback.Stop();

            string recordPart = recordingStoppedEarly
                ? $"Recorded {recordedLength.TotalSeconds:0.0}s (stopped early)"
                : $"Recorded {recordedLength.TotalSeconds:0.0}s";
            string playbackPart = playbackStoppedEarly ? "; playback stopped early" : "";
            return TestOutcome.Ok($"{recordPart} from \"{inputName}\" and played it back through \"{outputName}\"{playbackPart}.");
        }
        catch (Exception ex)
        {
            return TestOutcome.Fail($"Microphone test failed: {ex.Message}");
        }
    }

    private static WaveFormat GetMixFormat(MMDevice device)
    {
        using var client = device.AudioClient; // NAudio creates a fresh IAudioClient per access
        return client.MixFormat;
    }

    /// <summary>
    /// Adapts a recording to the output device's mix format. Shared-mode WASAPI rejects a stream whose channel
    /// count doesn't match the endpoint (E_INVALIDARG) — e.g. a 4-channel mic array played back on stereo speakers —
    /// so channels are matched first, then the sample rate if it differs.
    /// </summary>
    private static ISampleProvider ConvertToMixFormat(ISampleProvider source, WaveFormat mix)
    {
        if (source.WaveFormat.Channels != mix.Channels)
        {
            source = new ChannelMatchingSampleProvider(source, mix.Channels);
        }
        if (source.WaveFormat.SampleRate != mix.SampleRate)
        {
            source = new WdlResamplingSampleProvider(source, mix.SampleRate);
        }
        return source;
    }

    // Not NAudio's device.FriendlyName — see EndpointPropertyReader for why that's slow.
    private static string SafeName(MMDevice device)
    {
        using var reader = new EndpointPropertyReader();
        return reader.TryGetString(device.ID, PropertyKeys.PKEY_Device_FriendlyName) ?? device.ID;
    }
}
