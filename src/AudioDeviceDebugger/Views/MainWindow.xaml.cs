using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using AudioDeviceDebugger.Models;
using AudioDeviceDebugger.Services;
using AudioDeviceDebugger.ViewModels;

namespace AudioDeviceDebugger.Views;

public partial class MainWindow : Window
{
    private readonly IDeviceEnumerationService _enumerationService;
    private readonly IDeviceDetailsService _detailsService;
    private readonly IDefaultDeviceService _defaultDeviceService;
    private readonly IAudioTestService _testService;
    private readonly ILevelMeterService _levelMeterService;
    private readonly IEndpointVolumeService _volumeService;
    private readonly IAudioSessionService _sessionService;
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();

        _enumerationService = new DeviceEnumerationService();
        _detailsService = new DeviceDetailsService();
        _defaultDeviceService = new DefaultDeviceService();
        _testService = new AudioTestService();
        _levelMeterService = new LevelMeterService();
        _volumeService = new EndpointVolumeService();
        _sessionService = new AudioSessionService();

        _viewModel = new MainViewModel(_enumerationService, _defaultDeviceService, _detailsService, _testService, _levelMeterService, _volumeService, _sessionService);
        _viewModel.SelectedDeviceLevelChanged += (_, sample) => DetailGraph.AddSample(sample.MasterPeak);
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        DataContext = _viewModel;
        Closed += OnClosed;
        ApplySidePanelLayout();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedDevice))
        {
            DetailGraph.Clear();
        }
        else if (e.PropertyName is nameof(MainViewModel.IsPropertiesPanelOpen) or nameof(MainViewModel.IsEventsPanelOpen))
        {
            ApplySidePanelLayout();
        }
    }

    // Width the side panel had when last open (keeps the user's splitter position across collapse/expand).
    private GridLength _sidePanelWidth = new(3, GridUnitType.Star);
    private const double SidePanelMinWidth = 400;

    /// <summary>Shows the property table or the event log in the right-hand column, or collapses the column to zero
    /// width (just the toggle strips remain at the right edge). Done here rather than in XAML because the
    /// GridSplitter writes local Width values that would override style triggers.</summary>
    private void ApplySidePanelLayout()
    {
        bool properties = _viewModel.IsPropertiesPanelOpen;
        bool events = _viewModel.IsEventsPanelOpen;
        PropertiesList.Visibility = properties ? Visibility.Visible : Visibility.Collapsed;
        EventsPanel.Visibility = events ? Visibility.Visible : Visibility.Collapsed;

        if (properties || events)
        {
            SidePanelColumn.MinWidth = SidePanelMinWidth;
            SidePanelColumn.Width = _sidePanelWidth;
            SidePanelSplitter.Visibility = Visibility.Visible;
        }
        else
        {
            if (SidePanelColumn.Width.Value > 0)
            {
                _sidePanelWidth = SidePanelColumn.Width;
            }
            SidePanelColumn.MinWidth = 0;
            SidePanelColumn.Width = new GridLength(0);
            SidePanelSplitter.Visibility = Visibility.Collapsed;
        }
    }

    private void CopyEventsButton_Click(object sender, RoutedEventArgs e)
    {
        var events = _viewModel.EventLog.ShownEventsOldestFirst();
        if (events.Count == 0)
        {
            _viewModel.ShowStatus("No events to copy.");
            return;
        }
        try
        {
            Clipboard.SetText(EventLogExporter.ToText(events, EventLogFormat.PlainText));
            _viewModel.ShowStatus($"Copied {events.Count} event(s) to the clipboard.");
        }
        catch (Exception ex)
        {
            _viewModel.ShowStatus($"Couldn't copy the events: {ex.Message}");
        }
    }

    private void ExportEventsButton_Click(object sender, RoutedEventArgs e)
    {
        var events = _viewModel.EventLog.ShownEventsOldestFirst();
        if (events.Count == 0)
        {
            _viewModel.ShowStatus("No events to export.");
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export events",
            FileName = $"audio-events-{DateTime.Now:yyyyMMdd-HHmmss}",
            Filter = "CSV (*.csv)|*.csv|Markdown (*.md)|*.md|JSON Lines (*.jsonl)|*.jsonl",
            DefaultExt = ".csv",
            AddExtension = true,
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            using var writer = new StreamWriter(dialog.FileName, append: false, new System.Text.UTF8Encoding(false));
            EventLogExporter.Write(events, EventLogExporter.FormatFromExtension(dialog.FileName), writer);
            _viewModel.ShowStatus($"Exported {events.Count} event(s) to {dialog.FileName}.");
        }
        catch (Exception ex)
        {
            _viewModel.ShowStatus($"Couldn't export the events: {ex.Message}");
        }
    }

    private void ClearEventsButton_Click(object sender, RoutedEventArgs e) => _viewModel.EventLog.Clear();

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.Dispose();
        _levelMeterService.Dispose();
        _volumeService.Dispose();
        _sessionService.Dispose();
        _enumerationService.Dispose();
        (_detailsService as IDisposable)?.Dispose();
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => _viewModel.Refresh();

    private void TestButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: DeviceRowViewModel row })
        {
            _ = _viewModel.ToggleTestAsync(row);
        }
    }

    private void ChannelPlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ChannelPeakViewModel channel })
        {
            _ = _viewModel.ToggleChannelTestAsync(channel);
        }
    }

    private void CycleChannelsButton_Click(object sender, RoutedEventArgs e) =>
        _ = _viewModel.ToggleChannelTestAsync(null);

    private void SetDefaultButton_Click(object sender, RoutedEventArgs e) =>
        _viewModel.SetSelectedAsDefault(DefaultRoles.Default);

    private void SetCommsButton_Click(object sender, RoutedEventArgs e) =>
        _viewModel.SetSelectedAsDefault(DefaultRoles.Communications);

    private void SetBothButton_Click(object sender, RoutedEventArgs e) =>
        _viewModel.SetSelectedAsDefault(DefaultRoles.Both);
}
