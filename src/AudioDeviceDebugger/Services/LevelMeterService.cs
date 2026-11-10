using System.Timers;
using AudioDeviceDebugger.Models;
using NAudio.CoreAudioApi;

namespace AudioDeviceDebugger.Services;

public sealed class LevelMeterService : ILevelMeterService
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly Dictionary<string, Monitor> _monitors = new();
    private readonly object _lock = new();
    private bool _disposed;

    public event EventHandler<DeviceLevelChangedEventArgs>? LevelChanged;

    public void StartMonitoring(string deviceId, TimeSpan interval)
    {
        lock (_lock)
        {
            if (_disposed || _monitors.ContainsKey(deviceId)) return;

            MMDevice device;
            try
            {
                device = _enumerator.GetDevice(deviceId);
            }
            catch
            {
                // Device isn't present/active right now; nothing to meter until it reappears.
                return;
            }

            var timer = new Timer(interval.TotalMilliseconds) { AutoReset = true };
            timer.Elapsed += (_, _) => Poll(deviceId, device);

            _monitors[deviceId] = new Monitor(device, timer);
            timer.Start();
        }
    }

    public void StopMonitoring(string deviceId)
    {
        lock (_lock)
        {
            if (!_monitors.Remove(deviceId, out var monitor)) return;
            DisposeMonitor(monitor);
        }
    }

    private void Poll(string deviceId, MMDevice device)
    {
        try
        {
            var meter = device.AudioMeterInformation;
            var channelPeaks = new List<float>(meter.PeakValues.Count);
            for (int i = 0; i < meter.PeakValues.Count; i++)
            {
                channelPeaks.Add(meter.PeakValues[i]);
            }

            LevelChanged?.Invoke(this, new DeviceLevelChangedEventArgs(deviceId, new LevelSample
            {
                Timestamp = DateTime.UtcNow,
                MasterPeak = meter.MasterPeakValue,
                ChannelPeaks = channelPeaks,
            }));
        }
        catch
        {
            // Device likely went away (unplugged/disabled mid-session). The UI stops monitoring it once
            // a fresh DeviceEnumerationService refresh notices it's gone.
        }
    }

    private static void DisposeMonitor(Monitor monitor)
    {
        monitor.Timer.Stop();
        monitor.Timer.Dispose();
        monitor.Device.Dispose();
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var monitor in _monitors.Values)
            {
                DisposeMonitor(monitor);
            }
            _monitors.Clear();
        }
        _enumerator.Dispose();
    }

    private sealed record Monitor(MMDevice Device, Timer Timer);
}
