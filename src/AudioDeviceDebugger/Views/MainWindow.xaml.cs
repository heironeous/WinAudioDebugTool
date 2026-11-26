using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AudioDeviceDebugger.Interop;
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

        var logFile = new EventLogFile(message => Dispatcher.BeginInvoke(() => _viewModel?.ShowStatus(message)));

        _viewModel = new MainViewModel(_enumerationService, _defaultDeviceService, _detailsService, _testService, _levelMeterService, _volumeService, _sessionService,
            new DeviceInventoryService(), new BluetoothConnectionService(), logFile);
        _viewModel.SelectedDeviceLevelChanged += (_, sample) => DetailGraph.AddSample(sample.MasterPeak);
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        DataContext = _viewModel;
        Closed += OnClosed;
        StateChanged += (_, _) => _viewModel.IsWindowMinimized = WindowState == WindowState.Minimized;
        ApplySidePanelLayout();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyTitleBar();
        ThemeManager.ThemeChanged += OnThemeChanged;
    }

    private void OnThemeChanged(object? sender, EventArgs e) => ApplyTitleBar();

    private void ApplyTitleBar()
    {
        if (TryFindResource("SurfaceBase") is SolidColorBrush background)
        {
            WindowTheme.ApplyTitleBar(this, background.Color, dark: ThemeManager.Current == AppTheme.Dark,
                systemColors: ThemeManager.Current == AppTheme.HighContrast);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedDevice))
        {
            DetailGraph.Clear();
        }
        else if (e.PropertyName == nameof(MainViewModel.OpenSidePanel))
        {
            ApplySidePanelLayout();
        }
    }

    // Width the side panel had when last open (keeps the user's splitter position across collapse/expand).
    private GridLength _sidePanelWidth = new(3, GridUnitType.Star);
    private const double SidePanelMinWidth = 520; // the row of tabs

    /// <summary>Shows the open panel (properties, events, processes, raw recording or driver) in the right-hand column. With
    /// none open the column shrinks to the row of tabs, beside the header card, and the graph below takes the full
    /// width. Done here rather than in XAML because the GridSplitter writes local Width values that would override
    /// style triggers.</summary>
    private void ApplySidePanelLayout()
    {
        var panel = _viewModel.OpenSidePanel;
        PropertiesPanel.Visibility = panel == SidePanel.Properties ? Visibility.Visible : Visibility.Collapsed;
        EventsPanel.Visibility = panel == SidePanel.Events ? Visibility.Visible : Visibility.Collapsed;
        ProcessesPanel.Visibility = panel == SidePanel.Processes ? Visibility.Visible : Visibility.Collapsed;
        RecordingsPanel.Visibility = panel == SidePanel.Recordings ? Visibility.Visible : Visibility.Collapsed;
        DriverPanel.Visibility = panel == SidePanel.Driver ? Visibility.Visible : Visibility.Collapsed;

        bool open = panel != SidePanel.None;
        if (!open && !SidePanelColumn.Width.IsAuto)
        {
            _sidePanelWidth = SidePanelColumn.Width;
        }
        SidePanelColumn.MinWidth = open ? SidePanelMinWidth : 0;
        SidePanelColumn.Width = open ? _sidePanelWidth : GridLength.Auto;
        SidePanelSplitter.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        SidePanelHost.Margin = new Thickness(open ? 0 : 8, 0, 0, 0); // the splitter's width, when it isn't there
        Grid.SetRowSpan(SidePanelHost, open ? 2 : 1);
        Grid.SetColumnSpan(DetailsBody, open ? 1 : 3);
    }

    /// <summary>Gives the DEVICE column the width the other columns leave. A grouped DataGrid doesn't recompute star
    /// widths when its viewport changes (window resized, scrollbar shown or hidden), so this does.</summary>
    private void DeviceGrid_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.ViewportWidthChange == 0) return;
        double others = DeviceGrid.Columns.Where(c => c != DeviceColumn).Sum(c => c.ActualWidth);
        DeviceColumn.Width = new DataGridLength(Math.Max(DeviceColumn.MinWidth, e.ViewportWidth - others));
    }

    private void CopyIdButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedDevice is not { } row) return;
        try
        {
            Clipboard.SetText(row.Device.Id);
            _viewModel.ShowStatus($"Copied the endpoint ID of \"{row.Device.FriendlyName}\" to the clipboard.");
        }
        catch (Exception ex)
        {
            _viewModel.ShowStatus($"Couldn't copy the endpoint ID: {ex.Message}");
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

    private void CopyInventoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedDeviceInventory is not { } inventory) return;
        try
        {
            Clipboard.SetText(inventory.ToText());
            _viewModel.ShowStatus($"Copied the driver details of \"{inventory.EndpointName}\" to the clipboard.");
        }
        catch (Exception ex)
        {
            _viewModel.ShowStatus($"Couldn't copy the driver details: {ex.Message}");
        }
    }

    private void ClearEventsButton_Click(object sender, RoutedEventArgs e) => _viewModel.EventLog.Clear();

    private void OnClosed(object? sender, EventArgs e)
    {
        ThemeManager.ThemeChanged -= OnThemeChanged;
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

    private void RecordAbButton_Click(object sender, RoutedEventArgs e) =>
        _ = _viewModel.ToggleRecordAbAsync();

    private void TakePlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: TakeRowViewModel line })
        {
            _ = _viewModel.ToggleTakePlaybackAsync(line);
        }
    }

    // Picking a RECORDINGS line makes the channel strip play it. The ListBox's own selection is cleared straight
    // away: the highlight comes from the view model (which also moves it after an A/B recording), and a cleared
    // selection lets the same line be picked again later.
    private void TakesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox list || list.SelectedItem is not TakeRowViewModel line) return;
        _viewModel.SelectStripTake(line);
        Dispatcher.BeginInvoke(() => list.SelectedItem = null);
    }

    private void SetDefaultButton_Click(object sender, RoutedEventArgs e) =>
        _viewModel.SetSelectedAsDefault(DefaultRoles.Default);

    private void SetCommsButton_Click(object sender, RoutedEventArgs e) =>
        _viewModel.SetSelectedAsDefault(DefaultRoles.Communications);

    private void SetBothButton_Click(object sender, RoutedEventArgs e) =>
        _viewModel.SetSelectedAsDefault(DefaultRoles.Both);

    private async void BluetoothButton_Click(object sender, RoutedEventArgs e) =>
        await _viewModel.ToggleBluetoothConnectionAsync();
}
