using System.IO;
using AudioDeviceDebugger.Models;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace AudioDeviceDebugger.Services;

public sealed class AudioTestService : IAudioTestService
{
    private static readonly TimeSpan ToneDuration = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan RecordDuration = TimeSpan.FromSeconds(3);

    public Task<TestOutcome> PlayTestToneAsync(string outputDeviceId, CancellationToken cancellationToken = default) =>
        Task.Run(() => PlayTestTone(outputDeviceId, cancellationToken), cancellationToken);

    public Task<TestOutcome> RecordAndPlaybackAsync(string inputDeviceId, string? outputDeviceId, CancellationToken cancellationToken = default) =>
        Task.Run(() => RecordAndPlayback(inputDeviceId, outputDeviceId, cancellationToken), cancellationToken);

    private static TestOutcome PlayTestTone(string outputDeviceId, CancellationToken cancellationToken)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDevice(outputDeviceId);
            string deviceName = SafeName(device);

            using var output = new WasapiOut(device, AudioClientShareMode.Shared, true, 100);
            var tone = new SignalGenerator(44100, 2)
            {
                Type = SignalGeneratorType.Sin,
                Frequency = 440,
                Gain = 0.25,
            };

            output.Init(tone);
            output.Play();
            cancellationToken.WaitHandle.WaitOne(ToneDuration);
            output.Stop();

            cancellationToken.ThrowIfCancellationRequested();
            return TestOutcome.Ok($"Played a 440 Hz test tone through \"{deviceName}\" for {ToneDuration.TotalSeconds:0}s.");
        }
        catch (OperationCanceledException)
        {
            return TestOutcome.Fail("Test tone cancelled.");
        }
        catch (Exception ex)
        {
            return TestOutcome.Fail($"Failed to play test tone: {ex.Message}");
        }
    }

    private static TestOutcome RecordAndPlayback(string inputDeviceId, string? outputDeviceId, CancellationToken cancellationToken)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var inputDevice = enumerator.GetDevice(inputDeviceId);
            string inputName = SafeName(inputDevice);

            using var capture = new WasapiCapture(inputDevice);
            var recorded = new MemoryStream();
            capture.DataAvailable += (_, e) => recorded.Write(e.Buffer, 0, e.BytesRecorded);

            using var captureStopped = new ManualResetEventSlim(false);
            capture.RecordingStopped += (_, _) => captureStopped.Set();

            capture.StartRecording();
            cancellationToken.WaitHandle.WaitOne(RecordDuration);
            capture.StopRecording();
            captureStopped.Wait(TimeSpan.FromSeconds(2));

            cancellationToken.ThrowIfCancellationRequested();

            if (recorded.Length == 0)
            {
                return TestOutcome.Fail($"No audio captured from \"{inputName}\" — check it's active and not muted.");
            }

            recorded.Position = 0;

            using var outputDevice = outputDeviceId is null
                ? enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)
                : enumerator.GetDevice(outputDeviceId);
            string outputName = SafeName(outputDevice);

            using var playback = new WasapiOut(outputDevice, AudioClientShareMode.Shared, true, 100);
            var rawStream = new RawSourceWaveStream(recorded, capture.WaveFormat);
            playback.Init(rawStream);

            using var playbackStopped = new ManualResetEventSlim(false);
            playback.PlaybackStopped += (_, _) => playbackStopped.Set();

            playback.Play();
            playbackStopped.Wait(RecordDuration + TimeSpan.FromSeconds(1));

            return TestOutcome.Ok($"Recorded {RecordDuration.TotalSeconds:0}s from \"{inputName}\" and played it back through \"{outputName}\".");
        }
        catch (OperationCanceledException)
        {
            return TestOutcome.Fail("Microphone test cancelled.");
        }
        catch (Exception ex)
        {
            return TestOutcome.Fail($"Microphone test failed: {ex.Message}");
        }
    }

    private static string SafeName(MMDevice device)
    {
        try
        {
            return device.FriendlyName;
        }
        catch
        {
            return device.ID;
        }
    }
}
