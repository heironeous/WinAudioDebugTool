using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace AudioDeviceDebugger.Interop;

/// <summary>Gives a window's title bar the app's colours (it is drawn by Windows, not by WPF).</summary>
internal static class WindowTheme
{
    private const int DwmwaUseImmersiveDarkMode = 20; // Windows 10 2004 and later
    private const int DwmwaCaptionColor = 35;         // Windows 11 and later
    private const int DwmwaColorDefault = unchecked((int)0xFFFFFFFF);

    /// <summary>Call once the window has a handle (OnSourceInitialized), and again when the theme changes. Dark or
    /// light caption text, and the window's background as the caption colour; with <paramref name="systemColors"/>
    /// (high contrast) Windows' own title bar. Older Windows versions don't know these attributes and return an
    /// error, which is ignored: the title bar just stays as it was.</summary>
    public static void ApplyTitleBar(Window window, Color caption, bool dark, bool systemColors)
    {
        IntPtr handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;

        int darkMode = dark && !systemColors ? 1 : 0;
        _ = DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref darkMode, sizeof(int));
        int colorRef = systemColors ? DwmwaColorDefault : caption.R | caption.G << 8 | caption.B << 16;
        _ = DwmSetWindowAttribute(handle, DwmwaCaptionColor, ref colorRef, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
