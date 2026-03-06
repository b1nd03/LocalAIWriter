using System.Windows;
using System.Windows.Media;
using LocalAIWriter.Interop;
using LocalAIWriter.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace LocalAIWriter.Services;

/// <summary>
/// Detects and applies Windows system theme (Light/Dark/High Contrast).
/// Monitors registry for real-time theme changes.
/// </summary>
public sealed class ThemeService : IDisposable
{
    private readonly ILogger<ThemeService> _logger;
    private AppTheme _currentTheme = AppTheme.System;
    private bool _isDark;

    /// <summary>Raised when the system theme changes.</summary>
    public event EventHandler<bool>? ThemeChanged;

    /// <summary>Gets whether dark mode is currently active.</summary>
    public bool IsDarkMode => _isDark;

    public ThemeService(ILogger<ThemeService> logger)
    {
        _logger = logger;
        _isDark = AcrylicHelper.IsSystemDarkTheme();
        SystemEvents.UserPreferenceChanged += OnSystemThemeChanged;
    }

    /// <summary>Applies the specified theme to the application.</summary>
    public void ApplyTheme(AppTheme theme)
    {
        _currentTheme = theme;
        _isDark = theme switch
        {
            AppTheme.Dark => true,
            AppTheme.Light => false,
            _ => AcrylicHelper.IsSystemDarkTheme()
        };

        var themeUri = _isDark
            ? "Resources/Themes/DarkTheme.xaml"
            : "Resources/Themes/LightTheme.xaml";

        Application.Current.Dispatcher.Invoke(() =>
        {
            var dict = Application.Current.Resources.MergedDictionaries;
            // Remove existing theme dictionaries
            for (int i = dict.Count - 1; i >= 0; i--)
            {
                if (dict[i].Source?.ToString().Contains("Theme") == true)
                    dict.RemoveAt(i);
            }

            dict.Add(new ResourceDictionary { Source = new Uri(themeUri, UriKind.Relative) });

            // Apply dark title bar to all windows
            foreach (Window window in Application.Current.Windows)
            {
                AcrylicHelper.TryApplyDarkTitleBar(window, _isDark);
            }
        });

        _logger.LogInformation("Theme applied: {Theme} (dark={IsDark})", theme, _isDark);
        ThemeChanged?.Invoke(this, _isDark);
    }

    public void Dispose()
    {
        SystemEvents.UserPreferenceChanged -= OnSystemThemeChanged;
    }

    private void OnSystemThemeChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.General && _currentTheme == AppTheme.System)
        {
            ApplyTheme(AppTheme.System);
        }
    }
}
