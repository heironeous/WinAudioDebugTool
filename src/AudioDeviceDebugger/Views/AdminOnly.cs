using System.Windows;
using System.Windows.Controls;
using AudioDeviceDebugger.Services;

namespace AudioDeviceDebugger.Views;

/// <summary>Marks a control that only works with administrator rights:
/// <c>local:AdminOnly.IsRequired="True"</c>. While the app runs without them the control is disabled for good
/// (elevation can't change while the process runs, so this replaces any IsEnabled binding) and its tooltip says how
/// to get them — after the control's own tooltip, when that is plain text. Running elevated, the attached property
/// does nothing.</summary>
public static class AdminOnly
{
    public static readonly DependencyProperty IsRequiredProperty = DependencyProperty.RegisterAttached(
        "IsRequired", typeof(bool), typeof(AdminOnly), new PropertyMetadata(false, OnIsRequiredChanged));

    public static bool GetIsRequired(DependencyObject element) => (bool)element.GetValue(IsRequiredProperty);

    public static void SetIsRequired(DependencyObject element, bool value) => element.SetValue(IsRequiredProperty, value);

    private static void OnIsRequiredChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element || e.NewValue is not true || AppElevation.IsElevated) return;

        element.IsEnabled = false;
        ToolTipService.SetShowOnDisabled(element, true);
        // A bound tooltip isn't resolved yet while the XAML is loading; combine on Loaded.
        if (element.IsLoaded) ApplyHint(element);
        else element.Loaded += OnLoaded;
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        var element = (FrameworkElement)sender;
        element.Loaded -= OnLoaded;
        ApplyHint(element);
    }

    private static void ApplyHint(FrameworkElement element) => element.ToolTip = WithAdminHint(element.ToolTip);

    /// <summary>The control's own tooltip text followed by the admin hint, or only the hint.</summary>
    internal static string WithAdminHint(object? ownToolTip) =>
        ownToolTip is string { Length: > 0 } own
            ? own + "\n\n" + AppElevation.AdminRequiredHint
            : AppElevation.AdminRequiredHint;
}
