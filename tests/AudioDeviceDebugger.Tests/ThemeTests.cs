using System.IO;
using System.Windows.Media;
using System.Xml.Linq;
using AudioDeviceDebugger.Views;

namespace AudioDeviceDebugger.Tests;

public class ThemeTests
{
    [Theory]
    [InlineData(new[] { "--theme=light" }, AppTheme.Light)]
    [InlineData(new[] { "--THEME=Dark" }, AppTheme.Dark)]
    [InlineData(new[] { "--theme", "high-contrast" }, AppTheme.HighContrast)]
    [InlineData(new[] { "--theme=system" }, null)]
    [InlineData(new[] { "--theme=purple" }, null)]
    [InlineData(new[] { "--other" }, null)]
    [InlineData(new string[0], null)]
    public void Command_line_override(string[] args, AppTheme? expected) =>
        Assert.Equal(expected, ThemeManager.ParseOverride(args));

    [Theory]
    [InlineData(null, false, false, AppTheme.Dark)]
    [InlineData(null, false, true, AppTheme.Light)]
    [InlineData(null, true, false, AppTheme.HighContrast)]
    [InlineData(null, true, true, AppTheme.HighContrast)]
    [InlineData(AppTheme.Light, true, false, AppTheme.Light)]
    public void Override_then_high_contrast_then_app_mode(AppTheme? overrideTheme, bool highContrast, bool light, AppTheme expected) =>
        Assert.Equal(expected, ThemeManager.Choose(overrideTheme, highContrast, light));

    // ---- the palettes ---------------------------------------------------------------------------------------------

    private static Dictionary<string, Color> Palette(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AudioDeviceDebugger.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var file = Path.Combine(dir.FullName, "src", "AudioDeviceDebugger", "Views", "Themes", name + ".xaml");

        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        return XDocument.Load(file).Root!.Elements()
            .Where(e => e.Name.LocalName == "SolidColorBrush")
            .ToDictionary(e => (string)e.Attribute(x + "Key")!, e => (Color)ColorConverter.ConvertFromString((string)e.Attribute("Color")!));
    }

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public void Every_palette_defines_exactly_the_palette_keys(string name) =>
        Assert.Equal(ThemeManager.PaletteKeys.Order(), Palette(name).Keys.Order());

    /// <summary>WCAG relative luminance and contrast ratio.</summary>
    private static double Contrast(Color a, Color b)
    {
        static double Channel(byte c)
        {
            double s = c / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        static double Luminance(Color c) => 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public void Text_is_readable_on_every_surface(string name)
    {
        var palette = Palette(name);
        var surfaces = new[] { "SurfaceBase", "SurfacePanel", "SurfaceRaised" };
        // Device rows (hovered or selected) use primary and secondary text only; tertiary is for headings and labels.
        var cases = new[] { "TextPrimary", "TextSecondary" }
            .SelectMany(t => surfaces.Concat(new[] { "RowHover", "RowSelected" }).Select(s => (t, s)))
            .Concat(surfaces.Select(s => ("TextTertiary", s)));
        foreach (var (text, surface) in cases)
        {
            double ratio = Contrast(palette[text], palette[surface]);
            Assert.True(ratio >= 4.5, $"{name}: {text} on {surface} is {ratio:0.00}:1, below 4.5:1");
        }
    }

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public void Coloured_text_is_readable_on_the_panels(string name)
    {
        var palette = Palette(name);
        foreach (var text in new[] { "Accent", "Positive", "Caution", "Negative" })
        {
            foreach (var surface in new[] { "SurfaceBase", "SurfacePanel" })
            {
                double ratio = Contrast(palette[text], palette[surface]);
                Assert.True(ratio >= 4.5, $"{name}: {text} on {surface} is {ratio:0.00}:1, below 4.5:1");
            }
        }
    }
}
