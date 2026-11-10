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
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();

        _enumerationService = new DeviceEnumerationService();
        _detailsService = new DeviceDetailsService();
        _defaultDeviceService = new DefaultDeviceService();
        _testService = new AudioTestService();
        _levelMeterService = new LevelMeterService();

        _viewModel = new MainViewModel(_enumerationService, _defaultDeviceService, _detailsService, _testService, _levelMeterService);
        _viewModel.SelectedDeviceLevelChanged += (_, sample) => DetailGraph.AddSample(sample.MasterPeak);
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        DataContext = _viewModel;
        Closed += OnClosed;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedDevice))
        {
            DetailGraph.Clear();
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.Dispose();
        _levelMeterService.Dispose();
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
