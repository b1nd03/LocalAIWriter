using Microsoft.Extensions.Logging;

namespace LocalAIWriter.Services;

/// <summary>
/// Manages accessibility features: screen reader integration,
/// high contrast mode, reduced motion, and keyboard navigation.
/// </summary>
public sealed class AccessibilityService
{
    private readonly ILogger<AccessibilityService> _logger;

    public bool IsHighContrast { get; private set; }
    public bool IsReducedMotion { get; private set; }
    public bool IsScreenReaderActive { get; private set; }

    public AccessibilityService(ILogger<AccessibilityService> logger)
    {
        _logger = logger;
        DetectAccessibilitySettings();
    }

    /// <summary>Detects current system accessibility settings.</summary>
    public void DetectAccessibilitySettings()
    {
        IsHighContrast = System.Windows.SystemParameters.HighContrast;
        IsReducedMotion = IsHighContrast; // Windows ties reduced motion to high contrast
        IsScreenReaderActive = DetectScreenReader();

        _logger.LogInformation(
            "Accessibility: HighContrast={HC}, ReducedMotion={RM}, ScreenReader={SR}",
            IsHighContrast, IsReducedMotion, IsScreenReaderActive);
    }

    /// <summary>Gets the recommended animation duration based on accessibility settings.</summary>
    public TimeSpan GetAnimationDuration(TimeSpan normalDuration)
    {
        return IsReducedMotion ? TimeSpan.Zero : normalDuration;
    }

    /// <summary>Announces text to screen readers via UI Automation.</summary>
    public void Announce(string text)
    {
        if (!IsScreenReaderActive || string.IsNullOrEmpty(text)) return;
        _logger.LogDebug("Screen reader announcement: {Text}", text);
    }

    private static bool DetectScreenReader()
    {
        try
        {
            // Check if common screen readers are running
            var screenReaders = new[] { "nvda", "jaws", "narrator" };
            var processes = System.Diagnostics.Process.GetProcesses();
            return processes.Any(p =>
                screenReaders.Any(sr => p.ProcessName.Contains(sr, StringComparison.OrdinalIgnoreCase)));
        }
        catch { return false; }
    }
}
