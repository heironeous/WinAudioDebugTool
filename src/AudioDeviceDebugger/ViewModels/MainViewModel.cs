using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Data;
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

        // The DataGrid binds to Devices, which WPF shows through this default view — filtering it hides rows
        // without removing them, so metering / volume / running tests are unaffected.
        _devicesView = CollectionViewSource.GetDefaultView(Devices);
        _devicesView.Filter = item => item is DeviceRowViewModel row && IsShown(row);

        Refresh();
    }

    public ObservableCollection<DeviceRowViewModel> Devices { get; } = new();

    private readonly ICollectionView _devicesView;

    // Visibility filters per non-active state. Active devices are always shown. Defaults: Disabled and NotPresent
    // (mostly stale endpoints from devices used once) hidden; Unplugged (real jacks with nothing plugged in) shown.
    private bool _showDisabledDevices;
    private bool _showNotPresentDevices;
    private bool _showUnpluggedDevices = true;

    public bool ShowDisabledDevices
    {
        get => _showDisabledDevices;
        set { if (SetField(ref _showDisabledDevices, value)) ApplyDeviceFilter(); }
    }

    public bool ShowNotPresentDevices
    {
        get => _showNotPresentDevices;
        set { if (SetField(ref _showNotPresentDevices, value)) ApplyDeviceFilter(); }
    }

    public bool ShowUnpluggedDevices
    {
        get => _showUnpluggedDevices;
        set { if (SetField(ref _showUnpluggedDevices, value)) ApplyDeviceFilter(); }
    }

    /// <summary>Number of devices in each state, shown next to its checkbox whether or not it's ticked.</summary>
    public int DisabledDeviceCount => CountInState(DeviceConnectionState.Disabled);
    public int NotPresentDeviceCount => CountInState(DeviceConnectionState.NotPresent);
    public int UnpluggedDeviceCount => CountInState(DeviceConnectionState.Unplugged);

    private int CountInState(DeviceConnectionState state) => Devices.Count(row => row.Device.State == state);

    private void ApplyDeviceFilter()
    {
        _devicesView.Refresh();
        if (SelectedDevice is null || !IsShown(SelectedDevice))
        {
            SelectedDevice = FirstShownDevice();
        }
    }

    private bool IsShown(DeviceRowViewModel row) => row.Device.State switch
    {
        DeviceConnectionState.Disabled => _showDisabledDevices,
        DeviceConnectionState.NotPresent => _showNotPresentDevices,
        DeviceConnectionState.Unplugged => _showUnpluggedDevices,
        _ => true,
    };

    private DeviceRowViewModel? FirstShownDevice() => Devices.FirstOrDefault(IsShown);

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

    private bool _isPropertiesPanelOpen;

    /// <summary>Whether the property table is expanded. The property store is only read while this is true:
    /// opening it fetches the selected device's properties, closing it drops them.</summary>
    public bool IsPropertiesPanelOpen
    {
        get => _isPropertiesPanelOpen;
        set
        {
            if (!SetField(ref _isPropertiesPanelOpen, value)) return;
            if (value)
            {
                LoadSelectedDeviceProperties(SelectedDevice);
            }
            else
            {
                SelectedDeviceProperties = null;
            }
        }
    }

    private IReadOnlyList<PropertyEntry>? _selectedDeviceProperties;

    /// <summary>The selected device's property-store dump; null while the panel is closed or still loading.</summary>
    public IReadOnlyList<PropertyEntry>? SelectedDeviceProperties
    {
        get => _selectedDeviceProperties;
        private set => SetField(ref _selectedDeviceProperties, value);
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
        var activeIds = latest.Where(d => d.State == DeviceConnectionState.Active).Select(d => d.Id).ToHashSet();

        // Stop metering anything that's gone *or* no longer active (for inputs this also closes the capture
        // stream the meter keeps open).
        foreach (var id in Devices.Select(row => row.Device.Id).Where(id => !activeIds.Contains(id)))
        {
            _levelMeterService.StopMonitoring(id);
        }

        foreach (var id in _volumeService.MonitoredDeviceIds.Where(id => !activeIds.Contains(id)).ToList())
        {
            _volumeService.StopMonitoring(id);
        }

        Devices.Clear();
        foreach (var info in latest
                     .OrderBy(d => d.Direction)
                     .ThenBy(d => d.FriendlyName, StringComparer.OrdinalIgnoreCase))
        {
            var row = new DeviceRowViewModel(info);
            ApplyTestState(row);
            if (info.State == DeviceConnectionState.Active)
            {
                _levelMeterService.StartMonitoring(info.Id, TimeSpan.FromMilliseconds(100));
                row.ApplyVolumeState(_volumeService.StartMonitoring(info.Id));
            }
            row.VolumeEditedByUser += OnRowVolumeEdited;
            row.MuteEditedByUser += OnRowMuteEdited;
            Devices.Add(row);
        }

        SelectedDevice = Devices.FirstOrDefault(d => d.Device.Id == previousId && IsShown(d)) ?? FirstShownDevice();
        OnPropertyChanged(nameof(DisabledDeviceCount));
        OnPropertyChanged(nameof(NotPresentDeviceCount));
        OnPropertyChanged(nameof(UnpluggedDeviceCount));
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

        string deviceName = row.Device.FriendlyName;
        _activeTests[id] = session;
        session.PhaseChanged += (_, phase) => _dispatcher.BeginInvoke(() =>
        {
            UpdateRowTestState(id);
            if (phase == AudioTestPhase.PlayingBack && _activeTests.ContainsKey(id))
            {
                SetStatus($"Playing back the recording from \"{deviceName}\"… click stop to end playback.");
            }
        });
        UpdateRowTestState(id);
        SetStatus(row.Device.Direction == AudioDirection.Output
            ? $"Playing test tone on \"{deviceName}\"…"
            : $"Recording from \"{deviceName}\"… click stop to end recording early and play it back.");

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
            UpdateRowTestState(id);
        }
    }

    // Rows are rebuilt on every Refresh, so a running test is tracked by device ID rather than by row.
    private void UpdateRowTestState(string deviceId)
    {
        var row = Devices.FirstOrDefault(d => d.Device.Id == deviceId);
        if (row is not null) ApplyTestState(row);
    }

    /// <summary>Syncs a row's Test button (Test ▶ / Test ■ / Playback ■) with any test running on its device.</summary>
    private void ApplyTestState(DeviceRowViewModel row)
    {
        _activeTests.TryGetValue(row.Device.Id, out var session);
        row.IsTesting = session is not null;
        row.IsPlayingBack = session?.Phase == AudioTestPhase.PlayingBack;
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

        LoadSelectedDeviceProperties(row);
    }

    /// <summary>Fetches the property dump for <paramref name="row"/> in the background — only if the properties
    /// panel is open. Results that arrive after the selection changed or the panel closed are discarded.</summary>
    private void LoadSelectedDeviceProperties(DeviceRowViewModel? row)
    {
        SelectedDeviceProperties = null;
        if (row is null || !IsPropertiesPanelOpen) return;

        string id = row.Device.Id;
        Task.Run(() =>
        {
            try
            {
                var properties = _detailsService.GetProperties(id);
                _dispatcher.BeginInvoke(() =>
                {
                    if (IsPropertiesPanelOpen && SelectedDevice?.Device.Id == id)
                    {
                        SelectedDeviceProperties = properties;
                    }
                });
            }
            catch (Exception ex)
            {
                _dispatcher.BeginInvoke(() => SetStatus($"Couldn't load device properties: {ex.Message}"));
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
