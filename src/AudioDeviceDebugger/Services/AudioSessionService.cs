using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using AudioDeviceDebugger.Models;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace AudioDeviceDebugger.Services;

/// <summary>
/// Lists the audio sessions (per-app stream groups) on each active endpoint by polling IAudioSessionManager2 from one
/// dedicated MTA thread — every 500 ms for all devices, plus an immediate rescan when Windows reports a new session —
/// and polls the session peak meters of one device ~10 times a second for the details panel.
/// </summary>
/// <remarks>
/// Polling rather than per-session IAudioSessionEvents: state changes are picked up within ~0.5 s, a full scan of the
/// owner's 4 active endpoints costs ~0.5 ms, and polling can't miss a notification (session events are known to stop
/// after an endpoint reset). A stream that starts and stops within one poll interval can be missed.
/// All COM objects live on this thread (see LevelMeterService for why they can't cross apartments).
/// </remarks>
public sealed class AudioSessionService : IAudioSessionService
{
    private static readonly TimeSpan ScanInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan PeakInterval = TimeSpan.FromMilliseconds(100);

    private readonly object _lock = new();
    private HashSet<string>? _deviceIds; // null until the first SetDevices: nothing is scanned or reported before
    private string? _peakDeviceId;
    private volatile bool _scanRequested = true;
    private readonly AutoResetEvent _wake = new(false);
    private readonly Thread _thread;
    private volatile bool _disposed;

    public event EventHandler<AudioSessionSnapshot>? SessionsUpdated;
    public event EventHandler<AudioSessionPeaks>? PeaksUpdated;

    public AudioSessionService()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "Audio session poller" };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }

    public void SetDevices(IReadOnlyCollection<string> deviceIds)
    {
        lock (_lock)
        {
            if (_deviceIds is not null && _deviceIds.SetEquals(deviceIds)) return;
            _deviceIds = new HashSet<string>(deviceIds);
        }
        _scanRequested = true;
        _wake.Set();
    }

    public void SetPeakDevice(string? deviceId)
    {
        lock (_lock)
        {
            _peakDeviceId = deviceId;
        }
        _wake.Set();
    }

    private void Run()
    {
        var endpoints = new Dictionary<string, Endpoint>();
        var processCache = new Dictionary<int, ProcessDetails>();
        Dictionary<string, IReadOnlyList<AudioSessionInfo>>? lastReported = null;
        var peakControls = new List<(string Key, AudioSessionControl Control)>();
        string? peakControlsDevice = null;
        var nextScan = DateTime.UtcNow;

        using var enumerator = new MMDeviceEnumerator();
        try
        {
            while (!_disposed)
            {
                HashSet<string>? wanted;
                string? peakDevice;
                lock (_lock)
                {
                    wanted = _deviceIds;
                    peakDevice = _peakDeviceId;
                }
                if (wanted is null)
                {
                    _wake.WaitOne(PeakInterval);
                    continue;
                }

                // Open session managers for newly watched endpoints, release the ones no longer watched.
                foreach (var id in endpoints.Keys.Where(id => !wanted.Contains(id)).ToList())
                {
                    endpoints[id].Dispose();
                    endpoints.Remove(id);
                }
                foreach (var id in wanted.Where(id => !endpoints.ContainsKey(id)))
                {
                    var endpoint = Endpoint.TryOpen(enumerator, id, OnSessionCreated);
                    if (endpoint is not null) endpoints[id] = endpoint;
                }

                var now = DateTime.UtcNow;
                bool peakDeviceChanged = peakDevice != peakControlsDevice;
                if (_scanRequested || now >= nextScan || peakDeviceChanged)
                {
                    _scanRequested = false;
                    nextScan = now + ScanInterval;

                    DisposeAll(peakControls);
                    peakControlsDevice = peakDevice;

                    var byDevice = new Dictionary<string, IReadOnlyList<AudioSessionInfo>>();
                    var seenPids = new HashSet<int>();
                    foreach (var (id, endpoint) in endpoints)
                    {
                        byDevice[id] = Scan(id, endpoint, processCache, seenPids,
                            id == peakDevice ? peakControls : null);
                    }
                    foreach (var pid in processCache.Keys.Where(pid => !seenPids.Contains(pid)).ToList())
                    {
                        processCache.Remove(pid);
                    }

                    if (lastReported is null || !SameSessions(lastReported, byDevice))
                    {
                        lastReported = byDevice;
                        SessionsUpdated?.Invoke(this, new AudioSessionSnapshot(DateTimeOffset.Now, byDevice));
                    }
                }

                if (peakDevice is not null && peakControls.Count > 0)
                {
                    var peaks = new Dictionary<string, float>();
                    foreach (var (key, control) in peakControls)
                    {
                        try
                        {
                            peaks[key] = control.AudioMeterInformation.MasterPeakValue;
                        }
                        catch
                        {
                            peaks[key] = 0f; // session went away; the next scan drops it
                        }
                    }
                    PeaksUpdated?.Invoke(this, new AudioSessionPeaks(peakDevice, peaks));
                }

                _wake.WaitOne(PeakInterval);
            }
        }
        finally
        {
            DisposeAll(peakControls);
            foreach (var endpoint in endpoints.Values) endpoint.Dispose();
        }
    }

    // Called by Windows on its own thread when an app opens a new session: just schedule a rescan. (Re-enumerating
    // from inside the callback would re-register the notification from within it, which Core Audio forbids.)
    private void OnSessionCreated(object sender, IAudioSessionControl newSession)
    {
        _scanRequested = true;
        _wake.Set();
    }

    /// <summary>Reads every session on one endpoint. For the peak device, the session controls are kept (added to
    /// <paramref name="keep"/>) so their meters can be read between scans; all others are released immediately.</summary>
    private static List<AudioSessionInfo> Scan(string deviceId, Endpoint endpoint, Dictionary<int, ProcessDetails> processCache,
        HashSet<int> seenPids, List<(string, AudioSessionControl)>? keep)
    {
        var result = new List<AudioSessionInfo>();
        SessionCollection sessions;
        try
        {
            endpoint.Manager.RefreshSessions();
            sessions = endpoint.Manager.Sessions;
        }
        catch
        {
            return result; // endpoint went away between the device refresh and now
        }

        for (int i = 0; i < sessions.Count; i++)
        {
            AudioSessionControl? control = null;
            bool kept = false;
            try
            {
                control = sessions[i];
                int pid = (int)control.GetProcessID;
                bool isSystem = control.IsSystemSoundsSession;
                string key = control.GetSessionInstanceIdentifier ?? $"{pid}#{i}";
                seenPids.Add(pid);

                if (!processCache.TryGetValue(pid, out var process))
                {
                    process = ProcessDetails.Resolve(pid);
                    processCache[pid] = process;
                }

                string? displayName = control.DisplayName;
                string appName = isSystem ? "System sounds"
                    : !string.IsNullOrWhiteSpace(displayName) && !displayName.StartsWith('@') ? displayName
                    : process.AppName;

                result.Add(new AudioSessionInfo
                {
                    Key = key,
                    DeviceId = deviceId,
                    ProcessId = pid,
                    AppName = appName,
                    ProcessName = isSystem ? null : process.ProcessName,
                    ExecutablePath = isSystem ? null : process.Path,
                    ProcessExited = !isSystem && process.Exited,
                    State = control.State switch
                    {
                        AudioSessionState.AudioSessionStateActive => AudioSessionActivity.Active,
                        AudioSessionState.AudioSessionStateExpired => AudioSessionActivity.Expired,
                        _ => AudioSessionActivity.Inactive,
                    },
                    IsSystemSounds = isSystem,
                    IsThisApp = !isSystem && pid == Environment.ProcessId,
                    Volume = control.SimpleAudioVolume.Volume,
                    Muted = control.SimpleAudioVolume.Mute,
                });

                if (keep is not null)
                {
                    keep.Add((key, control));
                    kept = true;
                }
            }
            catch
            {
                // A session can expire while being read; skip it.
            }
            finally
            {
                if (!kept) control?.Dispose();
            }
        }

        result.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
        return result;
    }

    private static bool SameSessions(Dictionary<string, IReadOnlyList<AudioSessionInfo>> a,
        Dictionary<string, IReadOnlyList<AudioSessionInfo>> b) =>
        a.Count == b.Count && a.All(pair => b.TryGetValue(pair.Key, out var other) && pair.Value.SequenceEqual(other));

    private static void DisposeAll(List<(string Key, AudioSessionControl Control)> controls)
    {
        foreach (var (_, control) in controls)
        {
            try { control.Dispose(); } catch { /* best effort */ }
        }
        controls.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _wake.Set();
        _thread.Join(TimeSpan.FromSeconds(1));
    }

    private sealed class Endpoint : IDisposable
    {
        private readonly MMDevice _device;
        private readonly AudioSessionManager.SessionCreatedDelegate _onCreated;

        private Endpoint(MMDevice device, AudioSessionManager manager, AudioSessionManager.SessionCreatedDelegate onCreated)
        {
            _device = device;
            Manager = manager;
            _onCreated = onCreated;
        }

        public AudioSessionManager Manager { get; }

        public static Endpoint? TryOpen(MMDeviceEnumerator enumerator, string id, AudioSessionManager.SessionCreatedDelegate onCreated)
        {
            MMDevice? device = null;
            try
            {
                device = enumerator.GetDevice(id);
                var manager = device.AudioSessionManager;
                manager.OnSessionCreated += onCreated;
                return new Endpoint(device, manager, onCreated);
            }
            catch
            {
                device?.Dispose();
                return null;
            }
        }

        public void Dispose()
        {
            try
            {
                Manager.OnSessionCreated -= _onCreated;
                Manager.Dispose();
            }
            catch
            {
                // Best effort.
            }
            _device.Dispose();
        }
    }

    /// <summary>Name/path of a process, resolved once per PID per scan cycle.</summary>
    private sealed record ProcessDetails(string AppName, string? ProcessName, string? Path, bool Exited)
    {
        public static ProcessDetails Resolve(int pid)
        {
            if (pid == 0) return new ProcessDetails("System", null, null, false);

            string? processName = null;
            bool exited = false;
            try
            {
                using var process = Process.GetProcessById(pid);
                processName = process.ProcessName;
            }
            catch (ArgumentException)
            {
                exited = true; // no such process any more (the session outlives it until it expires)
            }
            catch
            {
                // Access denied etc. — the path query below may still work.
            }

            string? path = QueryImagePath(pid);
            string? description = null;
            if (path is not null)
            {
                try
                {
                    description = FileVersionInfo.GetVersionInfo(path).FileDescription?.Trim();
                }
                catch
                {
                    // No version resource.
                }
            }

            string appName = !string.IsNullOrEmpty(description) ? description
                : processName ?? (path is not null ? System.IO.Path.GetFileNameWithoutExtension(path) : null)
                ?? (exited ? $"PID {pid} (exited)" : $"PID {pid}");
            return new ProcessDetails(appName, processName, path, exited);
        }

        private const uint ProcessQueryLimitedInformation = 0x1000;

        // Works for most processes, including elevated ones, where Process.MainModule throws access denied.
        private static string? QueryImagePath(int pid)
        {
            IntPtr handle = OpenProcess(ProcessQueryLimitedInformation, false, (uint)pid);
            if (handle == IntPtr.Zero) return null;
            try
            {
                var buffer = new StringBuilder(1024);
                int size = buffer.Capacity;
                return QueryFullProcessImageName(handle, 0, buffer, ref size) ? buffer.ToString(0, size) : null;
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inheritHandle, uint processId);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
