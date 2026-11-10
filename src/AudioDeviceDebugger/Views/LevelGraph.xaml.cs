using System.Windows;
using System.Windows.Controls;

namespace AudioDeviceDebugger.Views;

/// <summary>
/// A rolling time-series meter: call <see cref="AddSample"/> on the UI thread every time a new
/// IAudioMeterInformation reading comes in. Used both as the small per-row meter in the device list and
/// as the larger graph in the selected device's detail panel — same control, different size.
/// </summary>
public partial class LevelGraph : UserControl
{
    private const int MaxSamples = 150;
    private readonly Queue<double> _samples = new();

    public LevelGraph()
    {
        InitializeComponent();
        SizeChanged += (_, _) => Redraw();
    }

    public void AddSample(double peak0to1)
    {
        _samples.Enqueue(Math.Clamp(peak0to1, 0.0, 1.0));
        while (_samples.Count > MaxSamples)
        {
            _samples.Dequeue();
        }

        Redraw();
    }

    public void Clear()
    {
        _samples.Clear();
        GraphLine.Points.Clear();
    }

    private void Redraw()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0 || _samples.Count == 0) return;

        double stepX = ActualWidth / Math.Max(1, MaxSamples - 1);
        int startIndex = MaxSamples - _samples.Count;

        var points = new System.Windows.Media.PointCollection(_samples.Count);
        int i = 0;
        foreach (double sample in _samples)
        {
            double x = (startIndex + i) * stepX;
            double y = ActualHeight - (sample * ActualHeight);
            points.Add(new Point(x, y));
            i++;
        }

        GraphLine.Points = points;
    }
}
