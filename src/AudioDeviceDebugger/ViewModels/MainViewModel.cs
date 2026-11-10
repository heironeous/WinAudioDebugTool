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
    private readonly Dispatcher _dispatcher;

    public MainViewModel(
        IDeviceEnumerationService enumerationService,
        IDefaultDeviceService defaultDeviceService,
        IDeviceDetailsService detailsService,
        IAudioTestService testService,
        ILevelMeterService levelMeterService)
    {
        _enumerationService = enumerationService;
        _defaultDeviceService = defaultDeviceService;
        _detailsService = detailsService;
        _testService = testService;
        _levelMeterService = levelMeterService;
        _dispatcher = Dispatcher.CurrentDispatcher;

        _enumerationService.DevicesChanged += OnDevicesChanged;
        _levelMeterService.LevelChanged += OnLevelChanged;

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

        Devices.Clear();
        foreach (var info in latest
                     .OrderBy(d => d.Direction)
                     .ThenBy(d => d.FriendlyName, StringComparer.OrdinalIgnoreCase))
        {
            Devices.Add(new DeviceRowViewModel(info) { IsTesting = _activeTests.ContainsKey(info.Id) });
            if (info.State == DeviceConnectionState.Active)
            {
                _levelMeterService.StartMonitoring(info.Id, TimeSpan.FromMilliseconds(100));
            }
        }

        SelectedDevice = Devices.FirstOrDefault(d => d.Device.Id == previousId) ?? Devices.FirstOrDefault();
    }

    public void SetAsDefault(DeviceRowViewModel row)
    {
        try
        {
            _defaultDeviceService.SetDefaultDevice(row.Device.Id);
            SetStatus($"Set \"{row.Device.FriendlyName}\" as the default device.");
            Refresh();
        }
        catch (Exception ex)
        {
            SetStatus($"Failed to set default device: {ex.Message}");
        }
    }

    public void SetAsDefaultCommunications(DeviceRowViewModel row)
    {
        try
        {
            _defaultDeviceService.SetDefaultCommunicationsDevice(row.Device.Id);
            SetStatus($"Set \"{row.Device.FriendlyName}\" as the default communications device.");
            Refresh();
        }
        catch (Exception ex)
        {
            SetStatus($"Failed to set default communications device: {ex.Message}");
        }
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

    private void OnDevicesChanged(object? sender, EventArgs e) => _dispatcher.BeginInvoke(Refresh);

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
        foreach (var session in _activeTests.Values)
        {
            session.Stop();
        }

        _enumerationService.DevicesChanged -= OnDevicesChanged;
        _levelMeterService.LevelChanged -= OnLevelChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
