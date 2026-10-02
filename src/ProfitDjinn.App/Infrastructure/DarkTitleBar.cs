using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ProfitDjinn.App.Infrastructure;

/// <summary>Asks Windows 10/11 to draw the window's title bar dark, to match the Dark and Terminal themes.</summary>
public static class DarkTitleBar
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static void Apply(Window window, bool dark)
    {
        void Set()
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            int value = dark ? 1 : 0;
            // Older Windows builds without the attribute just ignore it.
            _ = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
        }
        if (new WindowInteropHelper(window).Handle == IntPtr.Zero) window.SourceInitialized += (_, _) => Set();
        else Set();
    }
}
