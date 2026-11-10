using AudioDeviceDebugger.Models;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace AudioDeviceDebugger.Services;

/// <summary>
/// Polls every monitored device's peak meter from one dedicated MTA thread.
/// </summary>
/// <remarks>
/// All Core Audio objects (enumerator, devices, meters, capture streams) are created, used and released on that
/// one thread. They can't be handed across apartments: an <c>IMMDevice</c> created on the WPF UI (STA) thread and
/// used from a timer thread fails every call with E_NOINTERFACE, because the interface has no proxy/stub.
/// </remarks>
public sealed class LevelMeterService : ILevelMeterService
{
    private static readonly TimeSpan RetryOpenAfter = TimeSpan.FromSeconds(2);

    private readonly object _lock = new();
    private readonly HashSet<string> _wanted = new();
    private readonly ManualResetEventSlim _wake = new(false);
    private readonly Thread _thread;
    private TimeSpan _interval = TimeSpan.FromMilliseconds(100);
    private volatile bool _disposed;

    public event EventHandler<DeviceLevelChangedEventArgs>? LevelChanged;

    public LevelMeterService()
    {
        _thread = new Thread(PollLoop) { IsBackground = true, Name = "Level meter poller" };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }

    public void StartMonitoring(string deviceId, TimeSpan interval)
    {
        lock (_lock)
        {
            _wanted.Add(deviceId);
            _interval = interval;
        }
    }

    public void StopMonitoring(string deviceId)
    {
        lock (_lock)
        {
            _wanted.Remove(deviceId);
        }
    }

    private void PollLoop()
    {
        var monitors = new Dictionary<string, Monitor>();
        var retryAfter = new Dictionary<string, DateTime>();
        using var enumerator = new MMDeviceEnumerator();
        try
        {
            while (!_disposed)
            {
                HashSet<string> wanted;
                TimeSpan interval;
                lock (_lock)
                {
                    wanted = new HashSet<string>(_wanted);
                    interval = _interval;
                }

                // Close what's no longer wanted (also ends the capture stream for inputs).
                foreach (var id in monitors.Keys.Where(id => !wanted.Contains(id)).ToList())
                {
                    monitors[id].Dispose();
                    monitors.Remove(id);
                }
                foreach (var id in retryAfter.Keys.Where(id => !wanted.Contains(id)).ToList())
                {
                    retryAfter.Remove(id);
                }

                // Open what's newly wanted; if a device can't be opened, don't hammer it every tick.
                var now = DateTime.UtcNow;
                foreach (var id in wanted)
                {
                    if (monitors.ContainsKey(id)) continue;
                    if (retryAfter.TryGetValue(id, out var notBefore) && now < notBefore) continue;

                    var monitor = Monitor.TryOpen(enumerator, id);
                    if (monitor is null)
                    {
                        retryAfter[id] = now + RetryOpenAfter;
                        continue;
                    }
                    retryAfter.Remove(id);
                    monitors[id] = monitor;
                }

                foreach (var (id, monitor) in monitors.ToList())
                {
                    if (!TryPoll(id, monitor))
                    {
                        // Device likely went away mid-session; drop it and let the retry logic reopen it if it's
                        // still wanted (the UI stops asking once a refresh notices it's gone).
                        monitor.Dispose();
                        monitors.Remove(id);
                        retryAfter[id] = DateTime.UtcNow + RetryOpenAfter;
                    }
                }

                _wake.Wait(interval);
            }
        }
        finally
        {
            foreach (var monitor in monitors.Values)
            {
                monitor.Dispose();
            }
        }
    }

    private bool TryPoll(string deviceId, Monitor monitor)
    {
        LevelSample sample;
        try
        {
            var meter = monitor.Meter;
            var peaks = meter.PeakValues;
            var channelPeaks = new float[peaks.Count];
            for (int i = 0; i < channelPeaks.Length; i++)
            {
                channelPeaks[i] = peaks[i];
            }

            sample = new LevelSample
            {
                Timestamp = DateTime.UtcNow,
                MasterPeak = meter.MasterPeakValue,
                ChannelPeaks = channelPeaks,
            };
        }
        catch
        {
            return false;
        }

        LevelChanged?.Invoke(this, new DeviceLevelChangedEventArgs(deviceId, sample));
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _wake.Set();
        _thread.Join(TimeSpan.FromSeconds(2));
        _wake.Dispose();
    }

    /// <summary>One metered device. Owned by (and only touched from) the polling thread.</summary>
    private sealed class Monitor : IDisposable
    {
        private readonly MMDevice _device;
        private readonly WasapiCapture? _capture;

        private Monitor(MMDevice device, AudioMeterInformation meter, WasapiCapture? capture)
        {
            _device = device;
            Meter = meter;
            _capture = capture;
        }

        public AudioMeterInformation Meter { get; }

        public static Monitor? TryOpen(MMDeviceEnumerator enumerator, string deviceId)
        {
            MMDevice? device = null;
            try
            {
                device = enumerator.GetDevice(deviceId);
                if (device.State != DeviceState.Active)
                {
                    device.Dispose();
                    return null;
                }

                var meter = device.AudioMeterInformation;
                var capture = device.DataFlow == DataFlow.Capture ? TryStartSilentCapture(device) : null;
                return new Monitor(device, meter, capture);
            }
            catch
            {
                device?.Dispose();
                return null;
            }
        }

        /// <summary>Opens a shared-mode capture stream whose data is thrown away, purely so Windows feeds the
        /// endpoint's peak meter. Fails (returns null) e.g. if another app holds the mic in exclusive mode or mic
        /// access is blocked in Windows privacy settings — the meter then just reads 0.</summary>
        private static WasapiCapture? TryStartSilentCapture(MMDevice device)
        {
            WasapiCapture? capture = null;
            try
            {
                capture = new WasapiCapture(device);
                capture.DataAvailable += (_, _) => { };
                capture.StartRecording();
                return capture;
            }
            catch
            {
                capture?.Dispose();
                return null;
            }
        }

        public void Dispose()
        {
            try
            {
                _capture?.Dispose(); // stops recording and joins NAudio's capture thread
            }
            catch
            {
                // Best-effort; device may already be gone.
            }
            _device.Dispose();
        }
    }
}
