using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using System.Windows.Threading;
using AudioDeviceDebugger.Interop;
using AudioDeviceDebugger.Models;
using AudioDeviceDebugger.Services;

namespace AudioDeviceDebugger.ViewModels;

/// <summary>The panels that share the right-hand column of the details area.</summary>
public enum SidePanel
{
    None,
    Properties,
    Events,
    Processes,
    Recordings,
}

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IDeviceEnumerationService _enumerationService;
    private readonly IDefaultDeviceService _defaultDeviceService;
    private readonly IDeviceDetailsService _detailsService;
    private readonly IAudioTestService _testService;
    private readonly ILevelMeterService _levelMeterService;
    private readonly IEndpointVolumeService _volumeService;
    private readonly IAudioSessionService _sessionService;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _refreshDebounce;

    public MainViewModel(
        IDeviceEnumerationService enumerationService,
        IDefaultDeviceService defaultDeviceService,
        IDeviceDetailsService detailsService,
        IAudioTestService testService,
        ILevelMeterService levelMeterService,
        IEndpointVolumeService volumeService,
        IAudioSessionService sessionService)
    {
        _enumerationService = enumerationService;
        _defaultDeviceService = defaultDeviceService;
        _detailsService = detailsService;
        _testService = testService;
        _levelMeterService = levelMeterService;
        _volumeService = volumeService;
        _sessionService = sessionService;
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
        _enumerationService.EndpointNotified += OnEndpointNotified;
        _levelMeterService.LevelChanged += OnLevelChanged;
        _volumeService.VolumeChanged += OnExternalVolumeChanged;
        _sessionService.SessionsUpdated += OnSessionsUpdated;
        _sessionService.PeaksUpdated += OnSessionPeaks;

        // The DataGrid binds to Devices, which WPF shows through this default view — filtering it hides rows
        // without removing them, so metering / volume / running tests are unaffected.
        _devicesView = CollectionViewSource.GetDefaultView(Devices);
        _devicesView.Filter = item => item is DeviceRowViewModel row && IsShown(row);

        Refresh();
        LogStarted();
    }

    /// <summary>The event timeline (device, default, volume and test events), shown in the EVENTS panel.</summary>
    public EventLogViewModel EventLog { get; } = new();

    // Every endpoint ever seen (never pruned), so events for a removed device can still be named, and a state
    // change can show the state it changed *from* (the notification is logged before the debounced Refresh).
    private readonly Dictionary<string, DeviceInfo> _knownDevices = new();

    // Last known volume (whole %) and mute per endpoint, so a change can be logged as "from -> to".
    private readonly Dictionary<string, (int Percent, bool Muted)> _lastVolume = new();

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

    private bool _hasChannels;
    /// <summary>Whether the channel strip has anything to show (the selected device is active and its format known).</summary>
    public bool HasChannels
    {
        get => _hasChannels;
        private set => SetField(ref _hasChannels, value);
    }

    private bool _canCycleChannels;
    public bool CanCycleChannels
    {
        get => _canCycleChannels;
        private set => SetField(ref _canCycleChannels, value);
    }

    private string _cycleChannelsToolTip = "";
    public string CycleChannelsToolTip
    {
        get => _cycleChannelsToolTip;
        private set => SetField(ref _cycleChannelsToolTip, value);
    }

    private bool _isSelectedDeviceTesting;
    /// <summary>A test (whole-device or per-channel) is running on the selected device — the strip's buttons stop it.</summary>
    public bool IsSelectedDeviceTesting
    {
        get => _isSelectedDeviceTesting;
        private set => SetField(ref _isSelectedDeviceTesting, value);
    }

    /// <summary>Names the strip's channels from the device's mix format (speaker positions for outputs).</summary>
    private void ApplyChannelLayout(IReadOnlyList<SpeakerChannel> layout)
    {
        while (SelectedDeviceChannelPeaks.Count > layout.Count)
        {
            SelectedDeviceChannelPeaks.RemoveAt(SelectedDeviceChannelPeaks.Count - 1);
        }
        for (int i = 0; i < layout.Count; i++)
        {
            if (i >= SelectedDeviceChannelPeaks.Count)
            {
                SelectedDeviceChannelPeaks.Add(new ChannelPeakViewModel(i));
            }
            SelectedDeviceChannelPeaks[i].ShortName = layout[i].ShortName;
            SelectedDeviceChannelPeaks[i].LongName = layout[i].LongName;
        }
        RefreshChannelControls();
    }

    /// <summary>Recomputes which channel-strip buttons are usable, their tooltips, and the playing highlight.</summary>
    private void RefreshChannelControls()
    {
        var row = SelectedDevice;
        string id = row?.Device.Id ?? "";
        bool isActive = row?.IsActive == true;
        bool isOutput = row?.IsOutput == true;
        _activeTests.TryGetValue(id, out var running);
        var take = StripTake(id);
        bool canStart = isActive && (isOutput || take is not null);

        foreach (var vm in SelectedDeviceChannelPeaks)
        {
            vm.IsPlaying = running?.Channel == vm.Index;
            vm.CanPlay = running is not null || (canStart && (isOutput || vm.Index < take!.Channels));
            vm.PlayToolTip =
                running is not null ? "Stop the running test"
                : !isActive ? "The device isn't active"
                : isOutput ? $"Play the test music on {vm.LongName} ({vm.ShortName}) only"
                : take is not null ? $"Listen to {vm.LongName} of the {CaptureModeText.Name(take.Mode)} recording ({take.Duration.TotalSeconds:0.0} s) on the default output"
                : "Record first (Test on the device row, or Record A/B below) — then listen to each channel here";
        }

        HasChannels = SelectedDeviceChannelPeaks.Count > 0;
        IsSelectedDeviceTesting = running is not null;
        CanCycleChannels = HasChannels && (running is not null || canStart);
        CycleChannelsToolTip =
            running is not null ? "Stop the running test"
            : !isActive ? "The device isn't active"
            : isOutput ? "Play a short excerpt on each channel in turn"
            : take is not null ? $"Listen to each channel of the {CaptureModeText.Name(take.Mode)} recording in turn"
            : "Record first (Test on the device row, or Record A/B below) — then listen to each channel here";

        SyncTakeRows();
    }

    private DeviceRowViewModel? _selectedDevice;
    public DeviceRowViewModel? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (!SetField(ref _selectedDevice, value)) return;
            OnPropertyChanged(nameof(CanSetDefaults));
            EventLog.SelectedDeviceId = value?.Device.Id;
            // Hold a microphone open (so its meter is live) only while it's the selected row; selecting anything
            // else closes it. A running mic test keeps its own capture open regardless of selection.
            _levelMeterService.SetCaptureStreamDevice(
                value?.Device.Direction == AudioDirection.Input ? value.Device.Id : null);
            _sessionService.SetPeakDevice(value?.IsActive == true ? value.Device.Id : null);
            SyncSelectedSessions();
            LoadSelectedDeviceDetails(value);
        }
    }

    private DeviceDetails? _selectedDeviceDetails;
    public DeviceDetails? SelectedDeviceDetails
    {
        get => _selectedDeviceDetails;
        private set => SetField(ref _selectedDeviceDetails, value);
    }

    private SidePanel _openSidePanel;

    /// <summary>Which panel the right-hand column shows (the tab strip at the right edge picks it); None collapses
    /// the column. The property store is only read while the PROPERTIES panel is open: opening it fetches the
    /// selected device's properties, closing it drops them. Events are recorded whatever is shown.</summary>
    public SidePanel OpenSidePanel
    {
        get => _openSidePanel;
        set
        {
            var previous = _openSidePanel;
            if (!SetField(ref _openSidePanel, value)) return;
            OnPropertyChanged(nameof(IsPropertiesPanelOpen));
            OnPropertyChanged(nameof(IsEventsPanelOpen));
            OnPropertyChanged(nameof(IsProcessesPanelOpen));
            OnPropertyChanged(nameof(IsRecordingsPanelOpen));
            if (value == SidePanel.Properties)
            {
                LoadSelectedDeviceProperties(SelectedDevice);
            }
            else if (previous == SidePanel.Properties)
            {
                SelectedDeviceProperties = null;
            }
        }
    }

    // One bool per tab, for the strip's toggle buttons: checking one opens its panel (closing any other).
    public bool IsPropertiesPanelOpen { get => _openSidePanel == SidePanel.Properties; set => SetSidePanel(SidePanel.Properties, value); }
    public bool IsEventsPanelOpen { get => _openSidePanel == SidePanel.Events; set => SetSidePanel(SidePanel.Events, value); }
    public bool IsProcessesPanelOpen { get => _openSidePanel == SidePanel.Processes; set => SetSidePanel(SidePanel.Processes, value); }
    public bool IsRecordingsPanelOpen { get => _openSidePanel == SidePanel.Recordings; set => SetSidePanel(SidePanel.Recordings, value); }

    private void SetSidePanel(SidePanel panel, bool open)
    {
        if (open)
        {
            OpenSidePanel = panel;
        }
        else if (_openSidePanel == panel)
        {
            OpenSidePanel = SidePanel.None;
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

    // Last mic recording per input device and processing mode (Standard from the row's Test button; Raw, Call and
    // Standard from Record A/B), why a mode couldn't be recorded, and which recording the channel strip plays.
    private readonly Dictionary<string, Dictionary<CaptureMode, RecordedTake>> _takes = new();
    private readonly Dictionary<string, Dictionary<CaptureMode, string>> _takeErrors = new();
    private readonly Dictionary<string, CaptureMode> _stripMode = new();

    private RecordedTake? StripTake(string id) =>
        _takes.TryGetValue(id, out var byMode) && _stripMode.TryGetValue(id, out var mode) && byMode.TryGetValue(mode, out var take)
            ? take
            : null;

    private void StoreTakes(string id, IReadOnlyList<RecordedTake> takes, IReadOnlyDictionary<CaptureMode, string>? errors, CaptureMode stripPreference)
    {
        if (!_takes.TryGetValue(id, out var byMode)) _takes[id] = byMode = new();
        if (!_takeErrors.TryGetValue(id, out var errorsByMode)) _takeErrors[id] = errorsByMode = new();
        foreach (var take in takes)
        {
            byMode[take.Mode] = take;
            errorsByMode.Remove(take.Mode);
        }
        foreach (var (mode, error) in errors ?? new Dictionary<CaptureMode, string>())
        {
            byMode.Remove(mode); // an older take would be mistaken for this recording
            errorsByMode[mode] = error;
        }
        if (takes.Count > 0)
        {
            _stripMode[id] = takes.Any(t => t.Mode == stripPreference) ? stripPreference : takes[0].Mode;
        }
    }

    // ---- RECORDINGS (inputs): last take per processing mode, for A/B listening ----

    /// <summary>One fixed line per mode, in comparison order: Raw, Call, Standard.</summary>
    public ObservableCollection<TakeRowViewModel> SelectedDeviceTakes { get; } = new()
    {
        new TakeRowViewModel(CaptureMode.Raw),
        new TakeRowViewModel(CaptureMode.Communications),
        new TakeRowViewModel(CaptureMode.Standard),
    };

    private bool _showRecordings;
    /// <summary>The RECORDINGS section applies (an active input is selected).</summary>
    public bool ShowRecordings
    {
        get => _showRecordings;
        private set => SetField(ref _showRecordings, value);
    }

    private string _recordingsSummary = "";
    public string RecordingsSummary
    {
        get => _recordingsSummary;
        private set => SetField(ref _recordingsSummary, value);
    }

    private string _recordAbToolTip = "";
    public string RecordAbToolTip
    {
        get => _recordAbToolTip;
        private set => SetField(ref _recordAbToolTip, value);
    }

    private void SyncTakeRows()
    {
        var row = SelectedDevice;
        string id = row?.Device.Id ?? "";
        bool active = row?.IsActive == true;
        ShowRecordings = active && row!.Device.Direction == AudioDirection.Input;

        _activeTests.TryGetValue(id, out var running);
        _takes.TryGetValue(id, out var byMode);
        _takeErrors.TryGetValue(id, out var errors);
        var strip = StripTake(id);
        foreach (var line in SelectedDeviceTakes)
        {
            RecordedTake? take = byMode?.GetValueOrDefault(line.Mode);
            bool playing = running?.PlayingMode == line.Mode;
            bool canPlay = running is not null ? playing : take is not null && active;
            string tip = playing ? "Stop playback"
                : running is not null ? "A test is running on this device"
                : take is null ? "Not recorded yet"
                : $"Play the whole {line.Name} recording ({take.Duration.TotalSeconds:0.0} s) on the default output";
            line.Update(take, errors?.GetValueOrDefault(line.Mode), strip?.Mode == line.Mode, playing, canPlay, tip);
        }

        RecordingsSummary = SelectedDeviceDetails?.Id != id ? ""
            : SelectedDeviceDetails.RawProcessingSupported switch
            {
                true => "· the driver supports raw capture",
                false => "· the driver says it can't do raw capture (Raw may still be processed)",
                null => "",
            };
        RecordAbToolTip = running is not null
            ? "Stop the running test"
            : "Record 5 s from this mic in Raw, Call and Standard at the same time — speak while it records — " +
              "then play Raw and Call back to back on the default output.\n" +
              "Raw: straight from the mic. Call: what call apps (Teams, Webex…) receive, i.e. what the other side hears " +
              "before the app's own processing. Standard: what most other apps receive.";
    }

    /// <summary>Records the selected mic in every processing mode at once, then plays Raw and Call back to back —
    /// or stops the test running on it.</summary>
    public async Task ToggleRecordAbAsync()
    {
        var row = SelectedDevice;
        if (row is null || row.Device.Direction != AudioDirection.Input || !row.IsActive) return;

        string id = row.Device.Id;
        if (_activeTests.TryGetValue(id, out var running))
        {
            running.Stop();
            return;
        }

        string name = row.Device.FriendlyName;
        await RunTestAsync(id, name, _testService.StartModeComparison(id, null),
            $"Recording from \"{name}\" in Raw, Call and Standard at the same time — speak now… click Stop to end early.",
            "A/B recording (Raw, Call, Standard)");
    }

    /// <summary>Plays one whole recording of the selected mic — or stops the test running on it.</summary>
    public async Task ToggleTakePlaybackAsync(TakeRowViewModel line)
    {
        var row = SelectedDevice;
        if (row is null) return;

        string id = row.Device.Id;
        if (_activeTests.TryGetValue(id, out var running))
        {
            running.Stop();
            return;
        }
        if (line.Take is not { } take) return;

        string name = row.Device.FriendlyName;
        await RunTestAsync(id, name, _testService.StartTakePlayback(new[] { take }, name, null),
            $"Playing the {line.Name} recording of \"{name}\" — {CaptureModeText.Description(line.Mode)}…",
            $"Playback of the {line.Name} recording");
    }

    /// <summary>Makes the channel strip play the channels of this recording.</summary>
    public void SelectStripTake(TakeRowViewModel line)
    {
        var row = SelectedDevice;
        if (row is null || line.Take is null) return;
        _stripMode[row.Device.Id] = line.Mode;
        RefreshChannelControls();
    }

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
            _knownDevices[info.Id] = info;
            var row = new DeviceRowViewModel(info);
            ApplyTestState(row);
            if (info.State == DeviceConnectionState.Active)
            {
                _levelMeterService.StartMonitoring(info.Id, TimeSpan.FromMilliseconds(100));
                var volume = _volumeService.StartMonitoring(info.Id);
                row.ApplyVolumeState(volume);
                if (volume is { } v)
                {
                    _lastVolume[info.Id] = (ToPercent(v.Volume), v.Muted);
                }
            }
            row.ApplySessions(SessionsOf(info.Id));
            row.VolumeEditedByUser += OnRowVolumeEdited;
            row.MuteEditedByUser += OnRowMuteEdited;
            Devices.Add(row);
        }
        _sessionService.SetDevices(activeIds);

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
            LogForDevice(row.Device.Id, AudioEventKind.DefaultRequested, AudioEventSource.ThisApp, $"Set as the {roleText}");
        }
        catch (Exception ex)
        {
            SetStatus($"Failed to set \"{row.Device.FriendlyName}\" as the {roleText}: {ex.Message}");
            LogForDevice(row.Device.Id, AudioEventKind.Error, AudioEventSource.ThisApp, $"Failed to set as the {roleText}: {ex.Message}");
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
    /// phase (output: stop the test music; mic: stop recording and play back what was captured, or stop playback).</summary>
    public async Task ToggleTestAsync(DeviceRowViewModel row)
    {
        string id = row.Device.Id;
        if (_activeTests.TryGetValue(id, out var running))
        {
            running.Stop();
            return;
        }

        string name = row.Device.FriendlyName;
        if (row.Device.Direction == AudioDirection.Output)
        {
            await RunTestAsync(id, name, _testService.StartTestSound(id), $"Playing test music on \"{name}\"…", "Test music");
        }
        else
        {
            await RunTestAsync(id, name, _testService.StartRecordAndPlayback(id, null),
                $"Recording from \"{name}\"… click stop to end recording early and play it back.",
                "Mic test (record, then play back)");
        }
    }

    /// <summary>
    /// Channel strip: plays one channel (<paramref name="channel"/>) or every channel in turn (null) of the selected
    /// device — test music on that output channel only, or that channel of the last mic recording. If a test is
    /// already running on the device, this stops it instead.
    /// </summary>
    public async Task ToggleChannelTestAsync(ChannelPeakViewModel? channel)
    {
        var row = SelectedDevice;
        if (row is null) return;

        string id = row.Device.Id;
        if (_activeTests.TryGetValue(id, out var running))
        {
            running.Stop();
            return;
        }

        string name = row.Device.FriendlyName;
        var channels = channel is null
            ? SelectedDeviceChannelPeaks.Select(c => c.Index).ToList()
            : new List<int> { channel.Index };

        if (row.Device.Direction == AudioDirection.Output)
        {
            if (channels.Count == 0) return;
            string status = channel is null
                ? $"Playing the test music on each channel of \"{name}\" in turn…"
                : $"Playing the test music on {channel.LongName} ({channel.ShortName}) of \"{name}\" only…";
            string label = channel is null
                ? "Test music on each channel in turn"
                : $"Test music on {channel.LongName} ({channel.ShortName}) only";
            await RunTestAsync(id, name, _testService.StartChannelTestSound(id, channels), status, label);
        }
        else
        {
            if (StripTake(id) is not { } take) return;
            channels = channels.Where(c => c < take.Channels).ToList();
            if (channels.Count == 0) return;
            string mode = CaptureModeText.Name(take.Mode);
            string status = channel is null
                ? $"Playing each channel of the {mode} recording of \"{name}\" in turn…"
                : $"Playing {channel.LongName} of the {mode} recording of \"{name}\"…";
            string label = channel is null
                ? $"Each channel of the {mode} recording in turn"
                : $"{channel.LongName} of the {mode} recording";
            await RunTestAsync(id, name, _testService.StartChannelPlayback(take, name, channels, null), status, label);
        }
    }

    /// <summary>Tracks a running test (row button state, channel highlight, status) until it completes; keeps a
    /// mic test's recording for per-channel playback.</summary>
    private async Task RunTestAsync(string id, string deviceName, AudioTestSession session, string startStatus, string testLabel)
    {
        _activeTests[id] = session;
        LogForDevice(id, AudioEventKind.TestStarted, AudioEventSource.ThisApp, $"{testLabel} started");
        session.PhaseChanged += (_, phase) => _dispatcher.BeginInvoke(() =>
        {
            UpdateRowTestState(id);
            if (phase == AudioTestPhase.PlayingBack && _activeTests.ContainsKey(id))
            {
                SetStatus($"Playing back the recording from \"{deviceName}\"… click stop to end playback.");
            }
        });
        session.ChannelChanged += (_, channel) => _dispatcher.BeginInvoke(() =>
        {
            if (SelectedDevice?.Device.Id != id) return;
            foreach (var vm in SelectedDeviceChannelPeaks)
            {
                vm.IsPlaying = vm.Index == channel;
            }
            var playing = SelectedDeviceChannelPeaks.FirstOrDefault(vm => vm.Index == channel);
            if (playing is not null && _activeTests.ContainsKey(id))
            {
                SetStatus($"Channel test on \"{deviceName}\": now playing {playing.LongName} ({playing.ShortName})…");
            }
        });
        session.PlayingModeChanged += (_, mode) => _dispatcher.BeginInvoke(() =>
        {
            if (SelectedDevice?.Device.Id == id) SyncTakeRows();
            if (mode is { } m && _activeTests.ContainsKey(id))
            {
                SetStatus($"Playing the {CaptureModeText.Name(m)} recording of \"{deviceName}\" — {CaptureModeText.Description(m)}… click Stop to end.");
            }
        });
        UpdateRowTestState(id);
        RefreshChannelControls();
        SetStatus(startStatus);

        try
        {
            var outcome = await session.Completion;
            if (session.Take is { } take)
            {
                StoreTakes(id, new[] { take }, null, CaptureMode.Standard);
            }
            if (session.ModeTakes.Count > 0 || session.ModeErrors.Count > 0)
            {
                StoreTakes(id, session.ModeTakes, session.ModeErrors, CaptureMode.Raw);
            }
            SetStatus(outcome.Message);
            LogForDevice(id, outcome.Success ? AudioEventKind.TestFinished : AudioEventKind.Error,
                AudioEventSource.ThisApp, outcome.Message);
        }
        catch (Exception ex)
        {
            SetStatus($"Test failed: {ex.Message}");
            LogForDevice(id, AudioEventKind.Error, AudioEventSource.ThisApp, $"{testLabel} failed: {ex.Message}");
        }
        finally
        {
            _activeTests.Remove(id);
            UpdateRowTestState(id);
            RefreshChannelControls();
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
        RefreshChannelControls();
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
                        ApplyChannelLayout(details.SharedModeFormat?.ChannelLayout ?? Array.Empty<SpeakerChannel>());
                    }
                });
            }
            catch (Exception ex)
            {
                _dispatcher.BeginInvoke(() =>
                {
                    SetStatus($"Couldn't load device details: {ex.Message}");
                    LogForDevice(id, AudioEventKind.Error, AudioEventSource.ThisApp, $"Couldn't load device details: {ex.Message}");
                });
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
            RecordVolume(row.Device.Id, ToPercent(row.VolumePercent / 100.0), row.IsMuted, AudioEventSource.ThisApp, DateTimeOffset.Now);
        }
        catch (Exception ex)
        {
            SetStatus($"Failed to set volume on \"{row.Device.FriendlyName}\": {ex.Message}");
            LogForDevice(row.Device.Id, AudioEventKind.Error, AudioEventSource.ThisApp, $"Failed to set volume: {ex.Message}");
        }
    }

    private void OnRowMuteEdited(object? sender, EventArgs e)
    {
        if (sender is not DeviceRowViewModel row) return;
        try
        {
            _volumeService.SetMute(row.Device.Id, row.IsMuted);
            SetStatus($"{(row.IsMuted ? "Muted" : "Unmuted")} \"{row.Device.FriendlyName}\".");
            RecordVolume(row.Device.Id, ToPercent(row.VolumePercent / 100.0), row.IsMuted, AudioEventSource.ThisApp, DateTimeOffset.Now);
        }
        catch (Exception ex)
        {
            SetStatus($"Failed to {(row.IsMuted ? "mute" : "unmute")} \"{row.Device.FriendlyName}\": {ex.Message}");
            LogForDevice(row.Device.Id, AudioEventKind.Error, AudioEventSource.ThisApp,
                $"Failed to {(row.IsMuted ? "mute" : "unmute")}: {ex.Message}");
        }
    }

    private void OnExternalVolumeChanged(object? sender, EndpointVolumeChangedEventArgs e)
    {
        var timestamp = DateTimeOffset.Now;
        _dispatcher.BeginInvoke(() =>
        {
            Devices.FirstOrDefault(d => d.Device.Id == e.DeviceId)?.ApplyVolumeState(e.State);
            RecordVolume(e.DeviceId, ToPercent(e.State.Volume), e.State.Muted, AudioEventSource.Windows, timestamp);
        });
    }

    private void OnDevicesChanged(object? sender, EventArgs e) => _dispatcher.BeginInvoke(RequestRefresh);

    // ---- audio sessions ("used by") -----------------------------------------------------------------------------

    private Dictionary<string, IReadOnlyList<AudioSessionInfo>> _latestSessions = new();

    // Last seen state of every other-app session (key "deviceId|sessionKey"), to log streaming start/stop.
    private Dictionary<string, AudioSessionInfo> _sessionStates = new();
    private bool _sessionsBaselined;

    /// <summary>The selected device's sessions for the USED BY list: other apps streaming first, then idle ones,
    /// system sounds, and this app last.</summary>
    public ObservableCollection<SessionRowViewModel> SelectedDeviceSessions { get; } = new();

    private bool _showSessions;
    /// <summary>Whether the USED BY section applies (only active endpoints can have sessions).</summary>
    public bool ShowSessions
    {
        get => _showSessions;
        private set => SetField(ref _showSessions, value);
    }

    private string _sessionsSummary = "";
    public string SessionsSummary
    {
        get => _sessionsSummary;
        private set => SetField(ref _sessionsSummary, value);
    }

    private static IReadOnlyList<AudioSessionInfo> NoSessions { get; } = Array.Empty<AudioSessionInfo>();

    private IReadOnlyList<AudioSessionInfo> SessionsOf(string deviceId) =>
        _latestSessions.TryGetValue(deviceId, out var sessions) ? sessions : NoSessions;

    private void OnSessionsUpdated(object? sender, AudioSessionSnapshot snapshot) =>
        _dispatcher.BeginInvoke(() => ApplySessionSnapshot(snapshot));

    private void ApplySessionSnapshot(AudioSessionSnapshot snapshot)
    {
        LogSessionChanges(snapshot);
        _latestSessions = new Dictionary<string, IReadOnlyList<AudioSessionInfo>>(snapshot.ByDevice);
        foreach (var row in Devices)
        {
            row.ApplySessions(SessionsOf(row.Device.Id));
        }
        SyncSelectedSessions();
    }

    private void OnSessionPeaks(object? sender, AudioSessionPeaks peaks) => _dispatcher.BeginInvoke(() =>
    {
        if (SelectedDevice?.Device.Id != peaks.DeviceId) return;
        foreach (var row in SelectedDeviceSessions)
        {
            row.Peak = peaks.PeakByKey.TryGetValue(row.Key, out float peak) ? peak : 0f;
        }
    });

    /// <summary>Brings the USED BY list in line with the latest snapshot for the selected device — updating rows in
    /// place by session key, so live meters and the list position don't jump on every scan.</summary>
    private void SyncSelectedSessions()
    {
        var device = SelectedDevice;
        bool active = device?.IsActive == true;
        var ordered = (active ? SessionsOf(device!.Device.Id) : NoSessions)
            .Where(s => s.State != AudioSessionActivity.Expired)
            .OrderBy(s => s.IsThisApp)
            .ThenBy(s => s.IsSystemSounds)
            .ThenByDescending(s => s.State == AudioSessionActivity.Active)
            .ThenBy(s => s.AppName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var keys = ordered.Select(s => s.Key).ToHashSet();
        for (int i = SelectedDeviceSessions.Count - 1; i >= 0; i--)
        {
            if (!keys.Contains(SelectedDeviceSessions[i].Key)) SelectedDeviceSessions.RemoveAt(i);
        }
        for (int i = 0; i < ordered.Count; i++)
        {
            int existing = -1;
            for (int j = i; j < SelectedDeviceSessions.Count; j++)
            {
                if (SelectedDeviceSessions[j].Key == ordered[i].Key) { existing = j; break; }
            }
            if (existing < 0)
            {
                SelectedDeviceSessions.Insert(i, new SessionRowViewModel(ordered[i]));
            }
            else
            {
                if (existing != i) SelectedDeviceSessions.Move(existing, i);
                SelectedDeviceSessions[i].Update(ordered[i]);
            }
        }

        ShowSessions = active;
        var others = ordered.Where(s => !s.IsThisApp && !s.IsSystemSounds).ToList();
        int streaming = others.Count(s => s.State == AudioSessionActivity.Active);
        SessionsSummary = others.Count == 0
            ? "no other app has this device open"
            : $"{others.Count} app session{(others.Count == 1 ? "" : "s")}, {streaming} streaming";
    }

    /// <summary>Logs other apps starting / stopping a stream (Active ↔ Inactive, or a streaming session that went
    /// away). This app's own streams and the system-sounds session (every notification "ding") aren't logged.</summary>
    private void LogSessionChanges(AudioSessionSnapshot snapshot)
    {
        var current = new Dictionary<string, AudioSessionInfo>();
        foreach (var (deviceId, sessions) in snapshot.ByDevice)
        {
            foreach (var s in sessions.Where(s => !s.IsThisApp && !s.IsSystemSounds))
            {
                current[$"{deviceId}|{s.Key}"] = s;
            }
        }

        foreach (var (key, s) in current)
        {
            bool isStreaming = s.State == AudioSessionActivity.Active;
            bool wasStreaming = _sessionStates.TryGetValue(key, out var previous) && previous.State == AudioSessionActivity.Active;
            if (isStreaming && !wasStreaming)
            {
                string detail = _sessionsBaselined
                    ? $"{s.AppName} started streaming (PID {s.ProcessId})"
                    : $"{s.AppName} was already streaming when the log started (PID {s.ProcessId})";
                LogForDevice(s.DeviceId, AudioEventKind.SessionStarted, AudioEventSource.Windows, detail, snapshot.Timestamp);
            }
            else if (!isStreaming && wasStreaming)
            {
                string how = s.State == AudioSessionActivity.Expired ? " and closed its session" : "";
                LogForDevice(s.DeviceId, AudioEventKind.SessionStopped, AudioEventSource.Windows,
                    $"{s.AppName} stopped streaming{how} (PID {s.ProcessId})", snapshot.Timestamp);
            }
        }

        foreach (var (key, previous) in _sessionStates)
        {
            if (current.ContainsKey(key) || previous.State != AudioSessionActivity.Active) continue;
            string why = snapshot.ByDevice.ContainsKey(previous.DeviceId) ? "session closed" : "device no longer active";
            LogForDevice(previous.DeviceId, AudioEventKind.SessionStopped, AudioEventSource.Windows,
                $"{previous.AppName} stopped streaming ({why}, PID {previous.ProcessId})", snapshot.Timestamp);
        }

        _sessionStates = current;
        _sessionsBaselined = true;
    }

    // ---- event timeline ----------------------------------------------------------------------------------------

    private static int ToPercent(double scalar) => (int)Math.Round(scalar * 100.0);

    /// <summary>Logs a volume and/or mute change against the last known values. Consecutive volume changes on the
    /// same device from the same source within a second (a slider drag, held media keys) merge into one line.</summary>
    private void RecordVolume(string deviceId, int percent, bool muted, AudioEventSource source, DateTimeOffset timestamp)
    {
        bool known = _lastVolume.TryGetValue(deviceId, out var last);
        _lastVolume[deviceId] = (percent, muted);
        if (!known) return;

        if (last.Muted != muted)
        {
            LogForDevice(deviceId, AudioEventKind.MuteChanged, source, muted ? "Muted" : "Unmuted", timestamp);
        }
        if (last.Percent != percent)
        {
            int from = last.Percent;
            var entry = NewDeviceEvent(deviceId, AudioEventKind.VolumeChanged, source, VolumeDetail(from, percent, from, percent), timestamp)
                with { FromValue = from.ToString(), Low = Math.Min(from, percent), High = Math.Max(from, percent) };
            EventLog.AddOrMerge(entry, $"volume|{deviceId}|{source}", TimeSpan.FromSeconds(1),
                (existing, incoming) =>
                {
                    int start = int.Parse(existing.FromValue!);
                    int low = Math.Min(existing.Low ?? start, percent);
                    int high = Math.Max(existing.High ?? start, percent);
                    return incoming with
                    {
                        Timestamp = existing.Timestamp,
                        FromValue = existing.FromValue,
                        Low = low,
                        High = high,
                        Detail = VolumeDetail(start, percent, low, high),
                    };
                });
        }
    }

    /// <summary>"Volume 40 % → 65 %", plus the range when a burst went beyond its start and end values
    /// (e.g. "Volume 100 % → 100 % (dipped to 90 %)").</summary>
    private static string VolumeDetail(int from, int to, int low, int high)
    {
        string text = $"Volume {from} % → {to} %";
        bool dipped = low < Math.Min(from, to);
        bool peaked = high > Math.Max(from, to);
        return (dipped, peaked) switch
        {
            (true, true) => $"{text} (ranged {low}–{high} %)",
            (true, false) => $"{text} (dipped to {low} %)",
            (false, true) => $"{text} (peaked at {high} %)",
            _ => text,
        };
    }

    private void OnEndpointNotified(object? sender, EndpointNotification notification)
    {
        // Property changes are only recorded while their filter is on — checked here, on the notification thread,
        // so the constant stream of them isn't even marshalled to the UI otherwise.
        if (notification.Kind == EndpointNotificationKind.PropertyChanged && !EventLog.RecordPropertyChanges) return;
        _dispatcher.BeginInvoke(() => LogEndpointNotification(notification));
    }

    private void LogEndpointNotification(EndpointNotification n)
    {
        string? id = n.DeviceId;
        switch (n.Kind)
        {
            case EndpointNotificationKind.Added:
                LogForDevice(id, AudioEventKind.DeviceAdded, AudioEventSource.Windows, "Endpoint added", n.Timestamp);
                break;

            case EndpointNotificationKind.Removed:
                LogForDevice(id, AudioEventKind.DeviceRemoved, AudioEventSource.Windows, "Endpoint removed", n.Timestamp);
                break;

            case EndpointNotificationKind.StateChanged when id is not null && n.NewState is { } newState:
            {
                string detail = _knownDevices.TryGetValue(id, out var info)
                    ? $"{StateName(info.State)} → {StateName(newState)}"
                    : $"Now {StateName(newState)}";
                LogForDevice(id, AudioEventKind.StateChanged, AudioEventSource.Windows, detail, n.Timestamp);
                if (info is not null)
                {
                    _knownDevices[id] = CopyWithState(info, newState);
                }
                break;
            }

            case EndpointNotificationKind.DefaultChanged when n.Flow is { } flow && n.Role is { } role:
            {
                string flowText = flow == AudioDirection.Output ? "output" : "input";
                // The previous holder, from the last refresh (which hasn't run yet — it's debounced).
                var previous = _knownDevices.Values.FirstOrDefault(d =>
                    d.Direction == flow && d.Id != id
                    && (role == DefaultRole.Communications ? d.IsDefaultCommunicationsDevice : d.IsDefaultDevice));
                string was = previous is null ? "" : $" (was {previous.FriendlyName})";
                string Detail(string roles) => id is null
                    ? $"No default {flowText} for {roles} any more{was}"
                    : $"Became the default {flowText} for {roles}{was}";

                // Windows reports each role separately (Set Default = Console + Multimedia): merge them into one line.
                // Communications stays separate — its previous holder ("was") is usually a different device.
                var entry = NewDeviceEvent(id, AudioEventKind.DefaultChanged, AudioEventSource.Windows, Detail(role.ToString()), n.Timestamp)
                    with { FromValue = role.ToString(), Direction = flow };
                string roleGroup = role == DefaultRole.Communications ? "comms" : "default";
                EventLog.AddOrMerge(entry, $"default|{flow}|{id}|{roleGroup}", TimeSpan.FromMilliseconds(500), (existing, incoming) =>
                {
                    var roles = existing.FromValue!.Split(" + ").Append(incoming.FromValue!).Distinct().ToList();
                    string joined = string.Join(" + ", roles);
                    return existing with { FromValue = joined, Detail = Detail(joined) };
                });
                break;
            }

            case EndpointNotificationKind.PropertyChanged when id is not null && n.PropertyFormatId is { } fmtid && n.PropertyId is { } pid:
            {
                string? symbol = PropertyKeyNames.TryGetSymbol(fmtid, pid);
                string keyName = symbol is null ? PropertyKeyNames.FormatRaw(fmtid, pid) : PropertyKeyNames.ToDisplayName(symbol);
                var entry = NewDeviceEvent(id, AudioEventKind.PropertyChanged, AudioEventSource.Windows, $"{keyName} changed", n.Timestamp);
                EventLog.AddOrMerge(entry, $"property|{id}|{fmtid}|{pid}", TimeSpan.FromSeconds(1),
                    (existing, _) => existing);
                break;
            }
        }
    }

    private void LogStarted()
    {
        string DefaultName(AudioDirection direction, bool comms) =>
            Devices.FirstOrDefault(d => d.Device.Direction == direction
                                        && (comms ? d.Device.IsDefaultCommunicationsDevice : d.Device.IsDefaultDevice))
                ?.Device.FriendlyName ?? "none";

        int active = Devices.Count(d => d.IsActive);
        EventLog.Add(new AudioEvent
        {
            Timestamp = DateTimeOffset.Now,
            Kind = AudioEventKind.Started,
            Source = AudioEventSource.ThisApp,
            Detail = $"Started: {Devices.Count} endpoints, {active} active. "
                     + $"Default output: {DefaultName(AudioDirection.Output, false)}; "
                     + $"comms output: {DefaultName(AudioDirection.Output, true)}; "
                     + $"default input: {DefaultName(AudioDirection.Input, false)}; "
                     + $"comms input: {DefaultName(AudioDirection.Input, true)}.",
        });
    }

    private void LogForDevice(string? deviceId, AudioEventKind kind, AudioEventSource source, string detail,
        DateTimeOffset? timestamp = null) =>
        EventLog.Add(NewDeviceEvent(deviceId, kind, source, detail, timestamp ?? DateTimeOffset.Now));

    private AudioEvent NewDeviceEvent(string? deviceId, AudioEventKind kind, AudioEventSource source, string detail,
        DateTimeOffset timestamp)
    {
        var (name, direction) = DescribeDevice(deviceId);
        return new AudioEvent
        {
            Timestamp = timestamp,
            Kind = kind,
            Source = source,
            DeviceId = deviceId,
            DeviceName = name,
            Direction = direction,
            Detail = detail,
        };
    }

    /// <summary>Name and direction for an endpoint ID: from devices seen so far, else read directly (a device
    /// added since the last refresh). Endpoint IDs start "{0.0.0." for outputs and "{0.0.1." for inputs.</summary>
    private (string? Name, AudioDirection? Direction) DescribeDevice(string? deviceId)
    {
        if (deviceId is null) return (null, null);
        if (_knownDevices.TryGetValue(deviceId, out var info)) return (info.FriendlyName, info.Direction);

        AudioDirection? direction = deviceId.StartsWith("{0.0.0.", StringComparison.Ordinal) ? AudioDirection.Output
            : deviceId.StartsWith("{0.0.1.", StringComparison.Ordinal) ? AudioDirection.Input
            : null;
        string? name = null;
        try
        {
            name = _enumerationService.TryGetFriendlyName(deviceId);
        }
        catch
        {
            // Removed endpoints may no longer be readable; fall back to the ID.
        }
        return (name ?? deviceId, direction);
    }

    private static string StateName(DeviceConnectionState state) =>
        state == DeviceConnectionState.NotPresent ? "Not present" : state.ToString();

    private static DeviceInfo CopyWithState(DeviceInfo info, DeviceConnectionState state) => new()
    {
        Id = info.Id,
        FriendlyName = info.FriendlyName,
        Direction = info.Direction,
        State = state,
        IsDefaultDevice = info.IsDefaultDevice,
        IsDefaultCommunicationsDevice = info.IsDefaultCommunicationsDevice,
        FormFactor = info.FormFactor,
    };

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
                // Meter arrived before the format/layout: show generic names until ApplyChannelLayout renames them.
                SelectedDeviceChannelPeaks.Add(new ChannelPeakViewModel(i));
                RefreshChannelControls();
            }

            SelectedDeviceChannelPeaks[i].Peak = channelPeaks[i];
        }
    }

    private void SetStatus(string message) => StatusMessage = message;

    /// <summary>Shows a message in the status bar (for actions handled in the view, e.g. exporting the event log).</summary>
    public void ShowStatus(string message) => SetStatus(message);

    public void Dispose()
    {
        _refreshDebounce.Stop();
        foreach (var session in _activeTests.Values)
        {
            session.Stop();
        }

        _enumerationService.DevicesChanged -= OnDevicesChanged;
        _enumerationService.EndpointNotified -= OnEndpointNotified;
        _levelMeterService.LevelChanged -= OnLevelChanged;
        _volumeService.VolumeChanged -= OnExternalVolumeChanged;
        _sessionService.SessionsUpdated -= OnSessionsUpdated;
        _sessionService.PeaksUpdated -= OnSessionPeaks;
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
