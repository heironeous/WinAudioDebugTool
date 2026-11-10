using System.ComponentModel;
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

        _viewModel = new MainViewModel(_enumerationService, _defaultDeviceService, _detailsService, _testService, _levelMeterService, _volumeService);
        _viewModel.SelectedDeviceLevelChanged += (_, sample) => DetailGraph.AddSample(sample.MasterPeak);
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        DataContext = _viewModel;
        Closed += OnClosed;
        ApplyPropertiesPanelLayout();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedDevice))
        {
            DetailGraph.Clear();
        }
        else if (e.PropertyName == nameof(MainViewModel.IsPropertiesPanelOpen))
        {
            ApplyPropertiesPanelLayout();
        }
    }

    // Width the property table had when last open (keeps the user's splitter position across collapse/expand).
    private GridLength _propertiesColumnWidth = new(3, GridUnitType.Star);
    private const double PropertiesColumnMinWidth = 400;

    /// <summary>Collapses the property table to zero width (just the toggle strip remains at the right edge) or
    /// restores it. Done here rather than in XAML because the GridSplitter writes local Width values that would
    /// override style triggers.</summary>
    private void ApplyPropertiesPanelLayout()
    {
        if (_viewModel.IsPropertiesPanelOpen)
        {
            PropertiesColumn.MinWidth = PropertiesColumnMinWidth;
            PropertiesColumn.Width = _propertiesColumnWidth;
            PropertiesSplitter.Visibility = Visibility.Visible;
            PropertiesList.Visibility = Visibility.Visible;
        }
        else
        {
            if (PropertiesColumn.Width.Value > 0)
            {
                _propertiesColumnWidth = PropertiesColumn.Width;
            }
            PropertiesColumn.MinWidth = 0;
            PropertiesColumn.Width = new GridLength(0);
            PropertiesSplitter.Visibility = Visibility.Collapsed;
            PropertiesList.Visibility = Visibility.Collapsed;
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.Dispose();
        _levelMeterService.Dispose();
        _volumeService.Dispose();
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

    private void SetDefaultButton_Click(object sender, RoutedEventArgs e) =>
        _viewModel.SetSelectedAsDefault(DefaultRoles.Default);

    private void SetCommsButton_Click(object sender, RoutedEventArgs e) =>
        _viewModel.SetSelectedAsDefault(DefaultRoles.Communications);

    private void SetBothButton_Click(object sender, RoutedEventArgs e) =>
        _viewModel.SetSelectedAsDefault(DefaultRoles.Both);
}
