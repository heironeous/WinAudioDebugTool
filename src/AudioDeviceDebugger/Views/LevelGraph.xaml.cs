using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using AudioDeviceDebugger.Models;

namespace AudioDeviceDebugger.Views;

/// <summary>
/// A rolling time-series level meter on a dB scale (−60…0 dBFS, see <see cref="LevelScale"/>): call
/// <see cref="AddSample"/> on the UI thread with each linear IAudioMeterInformation peak. Draws labelled dB gridlines
/// and shows the latest level as a readout.
/// </summary>
public partial class LevelGraph : UserControl
{
    private const int MaxSamples = 150;
    private static readonly double[] GridlinesDb = { -12, -24, -36, -48 };

    private readonly Queue<double> _samples = new(); // dB-scaled bar fill, 0–1

    public LevelGraph()
    {
        InitializeComponent();
        SizeChanged += (_, _) =>
        {
            DrawGridlines();
            Redraw();
        };
    }

    /// <param name="peak">Linear peak amplitude, 0–1, as reported by Windows.</param>
    public void AddSample(double peak)
    {
        _samples.Enqueue(LevelScale.ToMeter(peak));
        while (_samples.Count > MaxSamples)
        {
            _samples.Dequeue();
        }

        LevelReadout.Text = LevelScale.Format(peak);
        Redraw();
    }

    public void Clear()
    {
        _samples.Clear();
        GraphLine.Points.Clear();
        LevelReadout.Text = "";
    }

    private void DrawGridlines()
    {
        GridCanvas.Children.Clear();
        if (ActualWidth <= 0 || ActualHeight <= 0) return;

        var gridBrush = (Brush)FindResource("BorderSubtle");
        var labelBrush = (Brush)FindResource("TextTertiary");
        var labelSize = (double)FindResource("FontCaption");

        foreach (double db in GridlinesDb)
        {
            double y = ActualHeight - LevelScale.DbToMeter(db) * ActualHeight;
            GridCanvas.Children.Add(new Line
            {
                X1 = 0, X2 = ActualWidth, Y1 = y, Y2 = y,
                Stroke = gridBrush, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 4, 4 },
            });

            var label = new TextBlock { Text = $"{db:0} dB", FontSize = labelSize, Foreground = labelBrush };
            Canvas.SetLeft(label, 4);
            Canvas.SetTop(label, y - 16);
            GridCanvas.Children.Add(label);
        }
    }

    private void Redraw()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0 || _samples.Count == 0) return;

        double stepX = ActualWidth / Math.Max(1, MaxSamples - 1);
        int startIndex = MaxSamples - _samples.Count;

        var points = new PointCollection(_samples.Count);
        int i = 0;
        foreach (double level in _samples)
        {
            double x = (startIndex + i) * stepX;
            double y = ActualHeight - (level * ActualHeight);
            points.Add(new Point(x, y));
            i++;
        }

        GraphLine.Points = points;
    }
}
