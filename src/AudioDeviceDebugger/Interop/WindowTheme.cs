using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace AudioDeviceDebugger.Interop;

/// <summary>Gives a window's title bar the app's dark colours (it is drawn by Windows, not by WPF).</summary>
internal static class WindowTheme
{
    private const int DwmwaUseImmersiveDarkMode = 20; // Windows 10 2004 and later
    private const int DwmwaCaptionColor = 35;         // Windows 11 and later

    /// <summary>Call once the window has a handle (OnSourceInitialized). Older Windows versions don't know these
    /// attributes and return an error, which is ignored: the title bar just stays as it was.</summary>
    public static void ApplyDarkTitleBar(Window window, Color caption)
    {
        IntPtr handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;

        int dark = 1;
        _ = DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));
        int colorRef = caption.R | caption.G << 8 | caption.B << 16;
        _ = DwmSetWindowAttribute(handle, DwmwaCaptionColor, ref colorRef, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
