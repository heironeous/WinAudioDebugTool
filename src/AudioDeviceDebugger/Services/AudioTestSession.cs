using AudioDeviceDebugger.Models;

namespace AudioDeviceDebugger.Services;

/// <summary>What a running test is doing right now.</summary>
public enum AudioTestPhase
{
    /// <summary>Output test: playing the test tone.</summary>
    PlayingTone,

    /// <summary>Input test, first phase: recording from the microphone.</summary>
    Recording,

    /// <summary>Input test, second phase: playing the recording back.</summary>
    PlayingBack,
}

/// <summary>
/// Handle to a running device test. A test runs through one or more phases (tone: play; mic: record, then
/// play back). <see cref="Stop"/> ends only the phase that is currently running — so stopping a mic test
/// while it records still plays back what was captured, and stopping again cuts the playback.
/// </summary>
public sealed class AudioTestSession
{
    private readonly object _lock = new();
    private CancellationTokenSource _stopCurrentPhase = new();
    private AudioTestPhase _phase;

    internal AudioTestSession(AudioTestPhase initialPhase, Func<AudioTestSession, TestOutcome> run)
    {
        _phase = initialPhase;
        Completion = Task.Run(() =>
        {
            try
            {
                return run(this);
            }
            finally
            {
                lock (_lock) _stopCurrentPhase.Dispose();
            }
        });
    }

    /// <summary>Completes with the test's outcome once every phase has finished.</summary>
    public Task<TestOutcome> Completion { get; }

    /// <summary>The phase the test is currently in.</summary>
    public AudioTestPhase Phase
    {
        get { lock (_lock) return _phase; }
    }

    /// <summary>Raised (on the test's worker thread) when the test moves to a new phase.</summary>
    public event EventHandler<AudioTestPhase>? PhaseChanged;

    /// <summary>Ends the currently running phase early.</summary>
    public void Stop()
    {
        lock (_lock)
        {
            try
            {
                _stopCurrentPhase.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Test already finished.
            }
        }
    }

    /// <summary>Token signalled when the user stops the current phase.</summary>
    internal CancellationToken CurrentStopToken
    {
        get { lock (_lock) return _stopCurrentPhase.Token; }
    }

    /// <summary>Moves to the next phase, so a <see cref="Stop"/> aimed at the previous phase doesn't carry over,
    /// and returns the new phase's stop token.</summary>
    internal CancellationToken BeginPhase(AudioTestPhase phase)
    {
        CancellationToken token;
        lock (_lock)
        {
            _stopCurrentPhase.Dispose();
            _stopCurrentPhase = new CancellationTokenSource();
            _phase = phase;
            token = _stopCurrentPhase.Token;
        }
        PhaseChanged?.Invoke(this, phase);
        return token;
    }
}
