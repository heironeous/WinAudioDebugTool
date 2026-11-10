using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using AudioDeviceDebugger.Models;
using AudioDeviceDebugger.Services;

namespace AudioDeviceDebugger.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IDeviceEnumerationService _enumerationService;
    private readonly IDefaultDeviceService _defaultDeviceService;
    private readonly IDeviceDetailsService _detailsService;
    private readonly IAudioTestService _testService;
    private readonly ILevelMeterService _levelMeterService;
    private readonly IEndpointVolumeService _volumeService;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _refreshDebounce;

    public MainViewModel(
        IDeviceEnumerationService enumerationService,
        IDefaultDeviceService defaultDeviceService,
        IDeviceDetailsService detailsService,
        IAudioTestService testService,
        ILevelMeterService levelMeterService,
        IEndpointVolumeService volumeService)
    {
        _enumerationService = enumerationService;
        _defaultDeviceService = defaultDeviceService;
        _detailsService = detailsService;
        _testService = testService;
        _levelMeterService = levelMeterService;
        _volumeService = volumeService;
        _dispatcher = Dispatcher.CurrentDispatcher;

        // A single default-device change fires several IMMNotificationClient callbacks (one per role/flow);
        // collapse each burst into one Refresh instead of rebuilding the list for every callback.
        _refreshDebounce = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(150),
        };
        _refreshDebounce.Tick += (_, _) =>
        {
            _refreshDebounce.Stop();
            Refresh();
        };

        _enumerationService.DevicesChanged += OnDevicesChanged;
        _levelMeterService.LevelChanged += OnLevelChanged;
        _volumeService.VolumeChanged += OnExternalVolumeChanged;

        Refresh();
    }

    public ObservableCollection<DeviceRowViewModel> Devices { get; } = new();

    /// <summary>Per-channel peaks for the currently selected device, for endpoints that report more than
    /// stereo (e.g. 5.1 surround). Resized in place as samples for the selection arrive.</summary>
    public ObservableCollection<ChannelPeakViewModel> SelectedDeviceChannelPeaks { get; } = new();

    private DeviceRowViewModel? _selectedDevice;
    public DeviceRowViewModel? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (!SetField(ref _selectedDevice, value)) return;
            OnPropertyChanged(nameof(CanSetDefaults));
            LoadSelectedDeviceDetails(value);
        }
    }

    private DeviceDetails? _selectedDeviceDetails;
    public DeviceDetails? SelectedDeviceDetails
    {
        get => _selectedDeviceDetails;
        private set => SetField(ref _selectedDeviceDetails, value);
    }

    private string? _statusMessage;
    public string? StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    private readonly Dictionary<string, AudioTestSession> _activeTests = new();

    /// <summary>Raised on the UI thread with a fresh sample whenever the *currently selected* device
    /// reports a new level — the view wires this straight to the big detail-panel graph.</summary>
    public event EventHandler<LevelSample>? SelectedDeviceLevelChanged;

    public void Refresh()
    {
        string? previousId = SelectedDevice?.Device.Id;
        var latest = _enumerationService.GetDevices();
        var latestIds = latest.Select(d => d.Id).ToHashSet();

        foreach (var stale in Devices.Where(row => !latestIds.Contains(row.Device.Id)).ToList())
        {
            _levelMeterService.StopMonitoring(stale.Device.Id);
        }

        var activeIds = latest.Where(d => d.State == DeviceConnectionState.Active).Select(d => d.Id).ToHashSet();
        foreach (var id in _volumeService.MonitoredDeviceIds.Where(id => !activeIds.Contains(id)).ToList())
        {
            _volumeService.StopMonitoring(id);
        }

        Devices.Clear();
        foreach (var info in latest
                     .OrderBy(d => d.Direction)
                     .ThenBy(d => d.FriendlyName, StringComparer.OrdinalIgnoreCase))
        {
            var row = new DeviceRowViewModel(info) { IsTesting = _activeTests.ContainsKey(info.Id) };
            if (info.State == DeviceConnectionState.Active)
            {
                _levelMeterService.StartMonitoring(info.Id, TimeSpan.FromMilliseconds(100));
                row.ApplyVolumeState(_volumeService.StartMonitoring(info.Id));
            }
            row.VolumeEditedByUser += OnRowVolumeEdited;
            row.MuteEditedByUser += OnRowMuteEdited;
            Devices.Add(row);
        }

        SelectedDevice = Devices.FirstOrDefault(d => d.Device.Id == previousId) ?? Devices.FirstOrDefault();
    }

    /// <summary>Default roles can only be assigned to an endpoint that's currently active — Windows rejects
    /// disabled / unplugged / not-present ones.</summary>
    public bool CanSetDefaults => SelectedDevice?.Device.State == DeviceConnectionState.Active;

    /// <summary>Assigns the given default role(s) to the currently selected device.</summary>
    public void SetSelectedAsDefault(DefaultRoles roles)
    {
        var row = SelectedDevice;
        if (row is null || roles == DefaultRoles.None) return;

        string roleText = roles switch
        {
            DefaultRoles.Default => "default device",
            DefaultRoles.Communications => "default communications device",
            _ => "default and default communications device",
        };

        try
        {
            if (roles.HasFlag(DefaultRoles.Default))
            {
                _defaultDeviceService.SetDefaultDevice(row.Device.Id);
            }
            if (roles.HasFlag(DefaultRoles.Communications))
            {
                _defaultDeviceService.SetDefaultCommunicationsDevice(row.Device.Id);
            }
            SetStatus($"Set \"{row.Device.FriendlyName}\" as the {roleText}.");
        }
        catch (Exception ex)
        {
            SetStatus($"Failed to set \"{row.Device.FriendlyName}\" as the {roleText}: {ex.Message}");
        }

        // Refresh even on partial failure (e.g. Default succeeded, Communications threw) so the Role column
        // reflects what Windows actually has now. Debounced: the change also triggers device notifications.
        RequestRefresh();
    }

    /// <summary>Schedules a Refresh shortly from now, restarting the delay if one is already pending.</summary>
    private void RequestRefresh()
    {
        _refreshDebounce.Stop();
        _refreshDebounce.Start();
    }

    /// <summary>Starts a test on the row's device, or — if one is already running there — stops its current
    /// phase (tone: stop playing; mic: stop recording and play back what was captured, or stop playback).</summary>
    public async Task ToggleTestAsync(DeviceRowViewModel row)
    {
        string id = row.Device.Id;
        if (_activeTests.TryGetValue(id, out var running))
        {
            running.Stop();
            return;
        }

        var session = row.Device.Direction == AudioDirection.Output
            ? _testService.StartTestTone(id)
            : _testService.StartRecordAndPlayback(id, null);

        _activeTests[id] = session;
        SetRowTesting(id, true);
        SetStatus(row.Device.Direction == AudioDirection.Output
            ? $"Playing test tone on \"{row.Device.FriendlyName}\"…"
            : $"Recording from \"{row.Device.FriendlyName}\"… click stop to end recording early and play it back.");

        try
        {
            var outcome = await session.Completion;
            SetStatus(outcome.Message);
        }
        catch (Exception ex)
        {
            SetStatus($"Test failed: {ex.Message}");
        }
        finally
        {
            _activeTests.Remove(id);
            SetRowTesting(id, false);
        }
    }

    // Rows are rebuilt on every Refresh, so a running test is tracked by device ID rather than by row.
    private void SetRowTesting(string deviceId, bool isTesting)
    {
        var row = Devices.FirstOrDefault(d => d.Device.Id == deviceId);
        if (row is not null) row.IsTesting = isTesting;
    }

    private void LoadSelectedDeviceDetails(DeviceRowViewModel? row)
    {
        SelectedDeviceDetails = null;
        SelectedDeviceChannelPeaks.Clear();
        if (row is null) return;

        string id = row.Device.Id;
        Task.Run(() =>
        {
            try
            {
                var details = _detailsService.GetDetails(id);
                _dispatcher.BeginInvoke(() =>
                {
                    if (SelectedDevice?.Device.Id == id)
                    {
                        SelectedDeviceDetails = details;
                    }
                });
            }
            catch (Exception ex)
            {
                _dispatcher.BeginInvoke(() => SetStatus($"Couldn't load device details: {ex.Message}"));
            }
        });
    }

    private void OnRowVolumeEdited(object? sender, EventArgs e)
    {
        if (sender is not DeviceRowViewModel row) return;
        try
        {
            _volumeService.SetVolume(row.Device.Id, (float)(row.VolumePercent / 100.0));
        }
        catch (Exception ex)
        {
            SetStatus($"Failed to set volume on \"{row.Device.FriendlyName}\": {ex.Message}");
        }
    }

    private void OnRowMuteEdited(object? sender, EventArgs e)
    {
        if (sender is not DeviceRowViewModel row) return;
        try
        {
            _volumeService.SetMute(row.Device.Id, row.IsMuted);
            SetStatus($"{(row.IsMuted ? "Muted" : "Unmuted")} \"{row.Device.FriendlyName}\".");
        }
        catch (Exception ex)
        {
            SetStatus($"Failed to {(row.IsMuted ? "mute" : "unmute")} \"{row.Device.FriendlyName}\": {ex.Message}");
        }
    }

    private void OnExternalVolumeChanged(object? sender, EndpointVolumeChangedEventArgs e)
    {
        _dispatcher.BeginInvoke(() =>
            Devices.FirstOrDefault(d => d.Device.Id == e.DeviceId)?.ApplyVolumeState(e.State));
    }

    private void OnDevicesChanged(object? sender, EventArgs e) => _dispatcher.BeginInvoke(RequestRefresh);

    private void OnLevelChanged(object? sender, DeviceLevelChangedEventArgs e)
    {
        _dispatcher.BeginInvoke(() =>
        {
            var row = Devices.FirstOrDefault(d => d.Device.Id == e.DeviceId);
            if (row is not null)
            {
                row.CurrentPeak = e.Sample.MasterPeak;
            }

            if (SelectedDevice?.Device.Id == e.DeviceId)
            {
                SelectedDeviceLevelChanged?.Invoke(this, e.Sample);
                UpdateSelectedDeviceChannelPeaks(e.Sample.ChannelPeaks);
            }
        });
    }

    private void UpdateSelectedDeviceChannelPeaks(IReadOnlyList<float> channelPeaks)
    {
        while (SelectedDeviceChannelPeaks.Count > channelPeaks.Count)
        {
            SelectedDeviceChannelPeaks.RemoveAt(SelectedDeviceChannelPeaks.Count - 1);
        }

        for (int i = 0; i < channelPeaks.Count; i++)
        {
            if (i >= SelectedDeviceChannelPeaks.Count)
            {
                SelectedDeviceChannelPeaks.Add(new ChannelPeakViewModel(i));
            }

            SelectedDeviceChannelPeaks[i].Peak = channelPeaks[i];
        }
    }

    private void SetStatus(string message) => StatusMessage = message;

    public void Dispose()
    {
        _refreshDebounce.Stop();
        foreach (var session in _activeTests.Values)
        {
            session.Stop();
        }

        _enumerationService.DevicesChanged -= OnDevicesChanged;
        _levelMeterService.LevelChanged -= OnLevelChanged;
        _volumeService.VolumeChanged -= OnExternalVolumeChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged(string? propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
