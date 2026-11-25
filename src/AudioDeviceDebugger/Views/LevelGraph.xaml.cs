using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using AudioDeviceDebugger.Models;

namespace AudioDeviceDebugger.Views;

/// <summary>
/// A rolling time-series level meter on a dB scale (−60…0 dBFS, see <see cref="LevelScale"/>): call
/// <see cref="AddSample"/> on the UI thread with each linear IAudioMeterInformation peak. Draws labelled dB gridlines,
/// the trace coloured by level (green, yellow from −12 dB, red from −3 dB), a peak-hold line, and shows the latest
/// level as a readout.
/// </summary>
public partial class LevelGraph : UserControl
{
    private const int MaxSamples = 150;
    private const double CautionDb = -12;
    private const double DangerDb = -3;
    private static readonly double[] GridlinesDb = { -12, -24, -36, -48 };

    // Samples arrive every 100 ms (the level meter's polling interval).
    private static readonly Duration SampleInterval = TimeSpan.FromMilliseconds(100);
    private const int PeakHoldSamples = 15;         // 1.5 s
    private const double PeakFallPerSample = 2 / 60.0; // 2 dB per sample = 20 dB/s, on the 0–1 scale

    private readonly Queue<double> _samples = new(); // dB-scaled bar fill, 0–1
    private double _peakHold;
    private int _samplesSincePeak;

    public LevelGraph()
    {
        InitializeComponent();
        SizeChanged += (_, _) =>
        {
            DrawBackground();
            GraphLine.Stroke = LevelBrush(top: 0xFF, bottom: 0xFF);
            GraphFill.Fill = LevelBrush(top: 0x60, bottom: 0x14);
            Redraw();
        };
    }

    /// <param name="peak">Linear peak amplitude, 0–1, as reported by Windows.</param>
    public void AddSample(double peak)
    {
        double level = LevelScale.ToMeter(peak);
        _samples.Enqueue(level);
        while (_samples.Count > MaxSamples)
        {
            _samples.Dequeue();
        }

        if (level >= _peakHold)
        {
            _peakHold = level;
            _samplesSincePeak = 0;
        }
        else if (++_samplesSincePeak > PeakHoldSamples)
        {
            _peakHold = Math.Max(level, _peakHold - PeakFallPerSample);
        }

        LevelReadout.Text = LevelScale.Format(peak);
        LevelReadout.Foreground = ZoneBrush(level);
        Redraw();
        ScrollIn();
    }

    public void Clear()
    {
        _samples.Clear();
        _peakHold = 0;
        _samplesSincePeak = 0;
        GraphLine.Points.Clear();
        GraphFill.Points.Clear();
        LevelReadout.Text = "";
        Scroll.BeginAnimation(TranslateTransform.XProperty, null);
        UpdatePeak();
    }

    private Color ThemeColor(string key) => ((SolidColorBrush)FindResource(key)).Color;

    private Brush ZoneBrush(double level) => (Brush)FindResource(
        level >= LevelScale.DbToMeter(DangerDb) ? "Negative"
        : level >= LevelScale.DbToMeter(CautionDb) ? "Caution"
        : "Positive");

    /// <summary>A vertical brush fixed to the dB scale, so each part of the trace has its level's colour whatever the
    /// trace's own height; opacity goes from <paramref name="bottom"/> at the floor to <paramref name="top"/> at 0 dB.</summary>
    private LinearGradientBrush LevelBrush(byte top, byte bottom)
    {
        double caution = LevelScale.DbToMeter(CautionDb), danger = LevelScale.DbToMeter(DangerDb);
        Color At(string key, double level)
        {
            var color = ThemeColor(key);
            color.A = (byte)(bottom + (top - bottom) * level);
            return color;
        }

        var brush = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            StartPoint = new Point(0, ActualHeight),
            EndPoint = new Point(0, 0),
            GradientStops =
            {
                new GradientStop(At("Positive", 0), 0),
                new GradientStop(At("Positive", caution), caution),
                new GradientStop(At("Caution", caution), caution),
                new GradientStop(At("Caution", danger), danger),
                new GradientStop(At("Negative", danger), danger),
                new GradientStop(At("Negative", 1), 1),
            },
        };
        brush.Freeze();
        return brush;
    }

    private void DrawBackground()
    {
        GridCanvas.Children.Clear();
        if (ActualWidth <= 0 || ActualHeight <= 0) return;

        AddZone("Negative", 0, DangerDb);
        AddZone("Caution", DangerDb, CautionDb);

        var gridBrush = (Brush)FindResource("BorderSubtle");
        var labelBrush = (Brush)FindResource("TextTertiary");
        var labelSize = (double)FindResource("FontCaption");

        // In a low graph the labels would overlap: then only every other one is shown, or none.
        double spacing = (LevelScale.DbToMeter(GridlinesDb[0]) - LevelScale.DbToMeter(GridlinesDb[1])) * ActualHeight;
        int labelEvery = spacing >= 18 ? 1 : spacing >= 9 ? 2 : 0;

        for (int i = 0; i < GridlinesDb.Length; i++)
        {
            double y = ActualHeight - LevelScale.DbToMeter(GridlinesDb[i]) * ActualHeight;
            GridCanvas.Children.Add(new Line
            {
                X1 = 0, X2 = ActualWidth, Y1 = y, Y2 = y,
                Stroke = gridBrush, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 4, 4 },
            });
            if (labelEvery == 0 || i % labelEvery != 0) continue;

            var label = new TextBlock { Text = $"{GridlinesDb[i]:0} dB", FontSize = labelSize, Foreground = labelBrush };
            Canvas.SetLeft(label, 6);
            Canvas.SetTop(label, y - 16);
            GridCanvas.Children.Add(label);
        }
    }

    /// <summary>A faint tint behind a level range.</summary>
    private void AddZone(string colorKey, double topDb, double bottomDb)
    {
        var color = ThemeColor(colorKey);
        color.A = 0x10;
        double y = (1 - LevelScale.DbToMeter(topDb)) * ActualHeight;
        var zone = new Rectangle
        {
            Width = ActualWidth,
            Height = (1 - LevelScale.DbToMeter(bottomDb)) * ActualHeight - y,
            Fill = new SolidColorBrush(color),
        };
        Canvas.SetTop(zone, y);
        GridCanvas.Children.Add(zone);
    }

    private double StepX => ActualWidth / Math.Max(1, MaxSamples - 1);

    private void Redraw()
    {
        UpdatePeak();
        if (ActualWidth <= 0 || ActualHeight <= 0 || _samples.Count == 0) return;

        double stepX = StepX;
        int startIndex = MaxSamples - _samples.Count;

        var line = new PointCollection(_samples.Count);
        int i = 0;
        foreach (double level in _samples)
        {
            double x = (startIndex + i) * stepX;
            double y = ActualHeight - (level * ActualHeight);
            line.Add(new Point(x, y));
            i++;
        }

        // The same outline, closed along the bottom edge.
        var area = new PointCollection(line);
        area.Add(new Point(line[^1].X, ActualHeight));
        area.Add(new Point(line[0].X, ActualHeight));

        GraphLine.Points = line;
        GraphFill.Points = area;
    }

    /// <summary>Slides the trace in by the one step it just moved, so it scrolls instead of jumping at each sample.</summary>
    private void ScrollIn()
    {
        if (!SystemParameters.ClientAreaAnimation) return;
        Scroll.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(StepX, 0, SampleInterval));
    }

    private void UpdatePeak()
    {
        bool shown = _peakHold > 0 && _samples.Count > 0;
        PeakLine.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        PeakReadout.Text = shown ? $"peak {LevelScale.FloorDb * (1 - _peakHold):0.0}" : "";
        if (!shown) return;

        double y = ActualHeight - _peakHold * ActualHeight;
        PeakLine.X1 = 0;
        PeakLine.X2 = ActualWidth;
        PeakLine.Y1 = PeakLine.Y2 = y;
        PeakLine.Stroke = ZoneBrush(_peakHold);
    }
}
