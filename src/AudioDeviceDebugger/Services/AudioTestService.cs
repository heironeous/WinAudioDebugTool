using System.IO;
using AudioDeviceDebugger.Interop;
using AudioDeviceDebugger.Models;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace AudioDeviceDebugger.Services;

public sealed class AudioTestService : IAudioTestService
{
    private static readonly TimeSpan RecordDuration = TimeSpan.FromSeconds(5);

    public AudioTestService()
    {
        _ = Task.Run(() => TestMusic.Prewarm());
    }

    public AudioTestSession StartTestSound(string outputDeviceId) =>
        new(AudioTestPhase.PlayingTestSound, session => PlayTestSound(outputDeviceId, session));

    public AudioTestSession StartRecordAndPlayback(string inputDeviceId, string? outputDeviceId) =>
        new(AudioTestPhase.Recording, session => RecordAndPlayback(inputDeviceId, outputDeviceId, session));

    public AudioTestSession StartChannelTestSound(string outputDeviceId, IReadOnlyList<int> channels) =>
        new(AudioTestPhase.PlayingTestSound, session => PlayChannelTestSound(outputDeviceId, channels, session));

    public AudioTestSession StartChannelPlayback(RecordedTake take, string inputName, IReadOnlyList<int> channels, string? outputDeviceId) =>
        new(AudioTestPhase.PlayingBack, session => PlayTakeChannels(take, inputName, channels, outputDeviceId, session));

    private static TestOutcome PlayTestSound(string outputDeviceId, AudioTestSession session)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDevice(outputDeviceId);
            string deviceName = SafeName(device);

            // Render the test music directly in the device's shared-mode mix format: shared-mode WASAPI won't open a
            // stream whose channel count differs from the device's (e.g. stereo on a 5.1 or mono endpoint).
            var mix = GetMixFormat(device);
            var (stoppedEarly, elapsed) = Play(device, TestMusic.Create(mix), TestMusic.Duration, session.CurrentStopToken);

            return stoppedEarly
                ? TestOutcome.Ok($"Stopped the test music on \"{deviceName}\" after {elapsed.TotalSeconds:0.0}s.")
                : TestOutcome.Ok($"Played the test music through \"{deviceName}\" ({TestMusic.Duration.TotalSeconds:0.0}s).");
        }
        catch (Exception ex)
        {
            return TestOutcome.Fail($"Failed to play test music: {ex.Message}");
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

            // Keep the take so each input channel can be replayed on its own later.
            session.SetTake(ToTake(recorded.ToArray(), capture.WaveFormat));

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

    /// <summary>Length of the excerpt each channel gets when cycling through all of them.</summary>
    private static readonly TimeSpan CycleExcerpt = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan CycleGap = TimeSpan.FromSeconds(0.25);

    /// <summary>Plays the test music (mono) on one output channel, or on each listed channel in turn, with every
    /// other channel silent. The LFE channel gets a low-passed version (a subwoofer can't reproduce the rest).</summary>
    private static TestOutcome PlayChannelTestSound(string outputDeviceId, IReadOnlyList<int> channels, AudioTestSession session)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDevice(outputDeviceId);
            string deviceName = SafeName(device);

            var mix = GetMixFormat(device);
            var layout = ChannelLayout.Describe(mix, isOutput: true);
            int sampleRate = mix.SampleRate;
            bool cycle = channels.Count > 1;

            var music = TestMusic.GetMono(sampleRate);
            int excerptFrames = cycle ? Math.Min(music.Length, (int)(CycleExcerpt.TotalSeconds * sampleRate)) : music.Length;
            int gapFrames = cycle ? (int)(CycleGap.TotalSeconds * sampleRate) : 0;

            var segments = channels
                .Select(c => new ChannelSegmentsSampleProvider.Segment(
                    c, ChannelTestSignal(music, excerptFrames, sampleRate, layout[c].IsLfe), gapFrames))
                .ToList();
            var provider = new ChannelSegmentsSampleProvider(segments, sampleRate, mix.Channels, routeToAllChannels: false);
            provider.ChannelStarted += session.SetChannel;

            var expected = TimeSpan.FromSeconds((double)provider.TotalFrames / sampleRate);
            var (stoppedEarly, elapsed) = Play(device, provider, expected, session.CurrentStopToken);
            session.SetChannel(null);

            string names = string.Join(", ", channels.Select(c => layout[c].ShortName));
            if (stoppedEarly)
            {
                return TestOutcome.Ok($"Stopped the channel test on \"{deviceName}\" after {elapsed.TotalSeconds:0.0}s.");
            }
            return cycle
                ? TestOutcome.Ok($"Played the test music on each channel of \"{deviceName}\" in turn: {names}.")
                : TestOutcome.Ok($"Played the test music on {layout[channels[0]].LongName} ({names}) only, on \"{deviceName}\".");
        }
        catch (Exception ex)
        {
            return TestOutcome.Fail($"Channel test failed: {ex.Message}");
        }
    }

    /// <summary>Plays one channel of a mic recording (or each listed channel in turn) in mono on every speaker of
    /// the output device.</summary>
    private static TestOutcome PlayTakeChannels(RecordedTake take, string inputName, IReadOnlyList<int> channels, string? outputDeviceId, AudioTestSession session)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var outputDevice = outputDeviceId is null
                ? enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)
                : enumerator.GetDevice(outputDeviceId);
            string outputName = SafeName(outputDevice);
            bool cycle = channels.Count > 1;
            int gapFrames = cycle ? (int)(CycleGap.TotalSeconds * take.SampleRate) : 0;

            var segments = channels
                .Select(c => new ChannelSegmentsSampleProvider.Segment(c, WithFades(take.GetChannel(c), take.SampleRate), gapFrames))
                .ToList();
            var provider = new ChannelSegmentsSampleProvider(segments, take.SampleRate, outputChannels: 1, routeToAllChannels: true);
            provider.ChannelStarted += session.SetChannel;

            var expected = TimeSpan.FromSeconds((double)provider.TotalFrames / take.SampleRate);
            var (stoppedEarly, elapsed) = Play(outputDevice, ConvertToMixFormat(provider, GetMixFormat(outputDevice)), expected, session.CurrentStopToken);
            session.SetChannel(null);

            string names = string.Join(", ", channels.Select(c => $"Ch {c + 1}"));
            if (stoppedEarly)
            {
                return TestOutcome.Ok($"Stopped channel playback of \"{inputName}\" after {elapsed.TotalSeconds:0.0}s.");
            }
            return TestOutcome.Ok(cycle
                ? $"Played each channel of the \"{inputName}\" recording in turn ({names}) through \"{outputName}\"."
                : $"Played {names} of the \"{inputName}\" recording through \"{outputName}\".");
        }
        catch (Exception ex)
        {
            return TestOutcome.Fail($"Channel playback failed: {ex.Message}");
        }
    }

    /// <summary>Plays <paramref name="provider"/> on <paramref name="device"/> until it ends by itself or the stop
    /// token fires. Returns whether it was stopped early, and how long it ran.</summary>
    private static (bool StoppedEarly, TimeSpan Elapsed) Play(MMDevice device, ISampleProvider provider, TimeSpan expected, CancellationToken stop)
    {
        using var output = new WasapiOut(device, AudioClientShareMode.Shared, true, 100);
        output.Init(provider);

        using var playbackStopped = new ManualResetEventSlim(false);
        output.PlaybackStopped += (_, _) => playbackStopped.Set();

        var started = DateTime.UtcNow;
        output.Play();
        int signalled = WaitHandle.WaitAny(new[] { playbackStopped.WaitHandle, stop.WaitHandle }, expected + TimeSpan.FromSeconds(1));
        output.Stop();
        return (signalled == 1, DateTime.UtcNow - started);
    }

    /// <summary>First <paramref name="frames"/> of the mono test music for one channel: low-passed and re-levelled
    /// for an LFE channel, with short fades so cutting an excerpt doesn't click.</summary>
    private static float[] ChannelTestSignal(float[] music, int frames, int sampleRate, bool isLfe)
    {
        var signal = music[..frames];
        if (isLfe)
        {
            // Two cascaded 2nd-order low-pass stages at 120 Hz (the usual LFE crossover), then back to the clip's level.
            var stage1 = NAudio.Dsp.BiQuadFilter.LowPassFilter(sampleRate, 120, 0.707f);
            var stage2 = NAudio.Dsp.BiQuadFilter.LowPassFilter(sampleRate, 120, 0.707f);
            float peak = 1e-6f;
            for (int i = 0; i < signal.Length; i++)
            {
                signal[i] = stage2.Transform(stage1.Transform(signal[i]));
                peak = Math.Max(peak, Math.Abs(signal[i]));
            }
            float gain = 0.35f / peak;
            for (int i = 0; i < signal.Length; i++) signal[i] *= gain;
        }
        return WithFades(signal, sampleRate);
    }

    private static float[] WithFades(float[] signal, int sampleRate)
    {
        int fadeIn = Math.Min(signal.Length / 2, sampleRate / 100);   // 10 ms
        int fadeOut = Math.Min(signal.Length / 2, sampleRate * 3 / 100); // 30 ms
        for (int i = 0; i < fadeIn; i++) signal[i] *= (float)i / fadeIn;
        for (int i = 0; i < fadeOut; i++) signal[signal.Length - 1 - i] *= (float)i / fadeOut;
        return signal;
    }

    /// <summary>Converts raw captured bytes (in the capture's format) to an interleaved float take.</summary>
    private static RecordedTake ToTake(byte[] bytes, WaveFormat format)
    {
        var provider = new RawSourceWaveStream(new MemoryStream(bytes), format).ToSampleProvider();
        var samples = new List<float>(bytes.Length / Math.Max(1, format.BitsPerSample / 8));
        var buffer = new float[format.Channels * 4096];
        int read;
        while ((read = provider.Read(buffer, 0, buffer.Length)) > 0)
        {
            for (int i = 0; i < read; i++) samples.Add(buffer[i]);
        }
        return new RecordedTake(samples.ToArray(), format.Channels, format.SampleRate);
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
