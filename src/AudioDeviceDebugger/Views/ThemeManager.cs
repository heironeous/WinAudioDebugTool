using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace AudioDeviceDebugger.Views;

public enum AppTheme
{
    Dark,
    Light,
    HighContrast,
}

/// <summary>
/// Picks the colour palette: Windows' high-contrast colours when high contrast is on, otherwise dark or light after
/// the Windows app mode (Settings → Personalisation → Colours → "Choose your app mode"). Follows changes while the app
/// runs. <c>--theme=dark|light|high-contrast</c> on the command line overrides it (for screenshots and testing).
/// </summary>
/// <remarks>The palette is the first merged dictionary of the application's resources; views refer to its keys with
/// DynamicResource, so replacing it repaints everything. Code that reads palette brushes itself listens to
/// <see cref="ThemeChanged"/>.</remarks>
public static class ThemeManager
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private static Application? _app;
    private static AppTheme? _override;

    public static AppTheme Current { get; private set; } = AppTheme.Dark;

    /// <summary>Raised on the UI thread after the palette was replaced.</summary>
    public static event EventHandler? ThemeChanged;

    /// <summary>The palette's keys: Dark.xaml and Light.xaml define all of them, the high-contrast palette too.</summary>
    public static readonly string[] PaletteKeys =
    {
        "SurfaceBase", "SurfacePanel", "SurfaceRaised", "RowHover", "RowSelected", "RowSelectedBorder", "RowMutedBorder",
        "RowDisabledBorder", "RowDisabledTint", "RowUnpluggedTint", "ControlPressed",
        "ControlTrack", "ControlOutline", "BorderSubtle", "Separator", "TextPrimary", "TextSecondary", "TextTertiary",
        "Accent", "AccentSoft", "Positive", "Caution", "Negative", "GraphPlate",
    };

    public static void Initialize(Application app, IEnumerable<string> args)
    {
        _app = app;
        _override = ParseOverride(args);
        Apply();
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color
                or UserPreferenceCategory.VisualStyle or UserPreferenceCategory.Accessibility)
            {
                app.Dispatcher.BeginInvoke(Apply);
            }
        };
        SystemParameters.StaticPropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SystemParameters.HighContrast)) app.Dispatcher.BeginInvoke(Apply);
        };
    }

    /// <summary>"--theme=light" (or "--theme light") → Light; null when absent, "system" or unknown.</summary>
    public static AppTheme? ParseOverride(IEnumerable<string> args)
    {
        var list = args.ToList();
        for (int i = 0; i < list.Count; i++)
        {
            string arg = list[i];
            string? value = arg.StartsWith("--theme=", StringComparison.OrdinalIgnoreCase) ? arg["--theme=".Length..]
                : arg.Equals("--theme", StringComparison.OrdinalIgnoreCase) && i + 1 < list.Count ? list[i + 1]
                : null;
            if (value is null) continue;
            return value.ToLowerInvariant() switch
            {
                "dark" => AppTheme.Dark,
                "light" => AppTheme.Light,
                "high-contrast" or "highcontrast" or "contrast" => AppTheme.HighContrast,
                _ => null,
            };
        }
        return null;
    }

    /// <summary>The palette for these settings: an override wins, then high contrast, then the app mode.</summary>
    public static AppTheme Choose(AppTheme? overrideTheme, bool highContrast, bool appsUseLightTheme) =>
        overrideTheme ?? (highContrast ? AppTheme.HighContrast : appsUseLightTheme ? AppTheme.Light : AppTheme.Dark);

    /// <summary>The Windows app mode; Windows' own default (no value) is light.</summary>
    private static bool AppsUseLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue("AppsUseLightTheme") is not int value || value != 0;
        }
        catch
        {
            return false;
        }
    }

    private static void Apply()
    {
        if (_app is null) return;
        var theme = Choose(_override, SystemParameters.HighContrast, AppsUseLightTheme());
        var palette = theme == AppTheme.HighContrast ? HighContrastPalette() : new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/AudioDeviceDebugger;component/Views/Themes/{theme}.xaml"),
        };

        var merged = _app.Resources.MergedDictionaries;
        if (merged.Count > 0 && merged[0].Contains("SurfaceBase"))
        {
            merged[0] = palette;
        }
        else
        {
            merged.Insert(0, palette);
        }

        bool changed = theme != Current;
        Current = theme;
        // Also on a high-contrast colour scheme change (same theme, new system colours).
        if (changed || theme == AppTheme.HighContrast) ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Windows' high-contrast colours: no translucency, the selection outlined in the highlight colour.</summary>
    private static ResourceDictionary HighContrastPalette()
    {
        Brush Solid(Color c)
        {
            var brush = new SolidColorBrush(c);
            brush.Freeze();
            return brush;
        }

        var window = SystemColors.WindowColor;
        var text = SystemColors.WindowTextColor;
        var highlight = SystemColors.HighlightColor;
        var link = SystemColors.HotTrackColor;
        var gray = SystemColors.GrayTextColor;

        return new ResourceDictionary
        {
            ["SurfaceBase"] = Solid(window),
            ["SurfacePanel"] = Solid(window),
            ["SurfaceRaised"] = Solid(window),
            ["RowHover"] = Solid(window),
            ["RowSelected"] = Solid(window),
            ["RowSelectedBorder"] = Solid(highlight),
            ["ControlPressed"] = Solid(highlight),
            ["ControlTrack"] = Solid(gray),
            ["ControlOutline"] = Solid(text),
            // No tints in high contrast; outlines only (the state line still says which state a row is in).
            ["RowMutedBorder"] = Solid(gray),
            ["RowDisabledBorder"] = Solid(text),
            ["RowDisabledTint"] = Solid(Colors.Transparent),
            ["RowUnpluggedTint"] = Solid(Colors.Transparent),
            ["BorderSubtle"] = Solid(text),
            ["Separator"] = Solid(gray),
            ["TextPrimary"] = Solid(text),
            ["TextSecondary"] = Solid(text),
            ["TextTertiary"] = Solid(text),
            ["Accent"] = Solid(link),
            ["AccentSoft"] = Solid(window),
            ["Positive"] = Solid(highlight),
            ["Caution"] = Solid(link),
            ["Negative"] = Solid(link),
            ["GraphPlate"] = Solid(window),
        };
    }
}
