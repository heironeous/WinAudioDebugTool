using AudioDeviceDebugger.Models;

namespace AudioDeviceDebugger.Services;

/// <summary>
/// Handle to a running device test. A test runs through one or more phases (tone: play; mic: record, then
/// play back). <see cref="Stop"/> ends only the phase that is currently running — so stopping a mic test
/// while it records still plays back what was captured, and stopping again cuts the playback.
/// </summary>
public sealed class AudioTestSession
{
    private readonly object _lock = new();
    private CancellationTokenSource _phase = new();

    internal AudioTestSession(Func<AudioTestSession, TestOutcome> run)
    {
        Completion = Task.Run(() =>
        {
            try
            {
                return run(this);
            }
            finally
            {
                lock (_lock) _phase.Dispose();
            }
        });
    }

    /// <summary>Completes with the test's outcome once every phase has finished.</summary>
    public Task<TestOutcome> Completion { get; }

    /// <summary>Ends the currently running phase early.</summary>
    public void Stop()
    {
        lock (_lock)
        {
            try
            {
                _phase.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Test already finished.
            }
        }
    }

    /// <summary>Token signalled when the user stops the current phase.</summary>
    internal CancellationToken CurrentPhase
    {
        get { lock (_lock) return _phase.Token; }
    }

    /// <summary>Moves to the next phase, so a <see cref="Stop"/> aimed at the previous phase doesn't carry over.</summary>
    internal CancellationToken BeginNextPhase()
    {
        lock (_lock)
        {
            _phase.Dispose();
            _phase = new CancellationTokenSource();
            return _phase.Token;
        }
    }
}
