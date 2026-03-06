using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows;
using System.Windows.Media;

namespace LocalAIWriter.Interop;

/// <summary>
/// Helpers for applying Windows 11 acrylic/mica backdrop effects to WPF windows.
/// Falls back gracefully on Windows 10.
/// </summary>
public static class AcrylicHelper
{
    /// <summary>
    /// Attempts to enable mica or acrylic backdrop on a WPF window.
    /// </summary>
    /// <param name="window">The WPF window to apply the effect to.</param>
    /// <param name="useMica">True for mica, false for acrylic.</param>
    /// <returns>True if the effect was applied successfully.</returns>
    public static bool TryApplyBackdrop(Window window, bool useMica = true)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return false;

            if (useMica)
            {
                int value = 1;
                NativeMethods.DwmSetWindowAttribute(hwnd,
                    NativeMethods.DWMWA_MICA_EFFECT,
                    ref value, sizeof(int));
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Applies the dark mode title bar to a WPF window.
    /// </summary>
    public static bool TryApplyDarkTitleBar(Window window, bool isDark)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return false;

            int value = isDark ? 1 : 0;
            NativeMethods.DwmSetWindowAttribute(hwnd,
                NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE,
                ref value, sizeof(int));

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Gets the current Windows system theme (light or dark).
    /// </summary>
    public static bool IsSystemDarkTheme()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            return value is int intVal && intVal == 0;
        }
        catch
        {
            return false;
        }
    }
}
