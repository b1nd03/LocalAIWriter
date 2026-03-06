using System.Windows.Automation;
using LocalAIWriter.Interop;
using Microsoft.Extensions.Logging;

namespace LocalAIWriter.Services;

/// <summary>
/// Tracks the caret (text cursor) position in the active application
/// using UIAutomation with Win32 fallback. DPI-aware for multi-monitor setups.
/// </summary>
public sealed class CaretPositionTracker
{
    private readonly ILogger<CaretPositionTracker> _logger;

    public CaretPositionTracker(ILogger<CaretPositionTracker> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Gets the screen-space position of the caret in the focused application.
    /// </summary>
    /// <returns>Screen coordinates, or null if caret cannot be found.</returns>
    public System.Windows.Point? GetCaretPosition()
    {
        // Method 1: UIAutomation
        var pos = TryGetViaAutomation();
        if (pos.HasValue) return pos;

        // Method 2: Win32 GetGUIThreadInfo
        pos = TryGetViaWin32();
        if (pos.HasValue) return pos;

        _logger.LogDebug("Could not determine caret position");
        return null;
    }

    private System.Windows.Point? TryGetViaAutomation()
    {
        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused == null) return null;

            if (focused.TryGetCurrentPattern(TextPattern.Pattern, out var textObj))
            {
                var textPattern = (TextPattern)textObj;
                var selection = textPattern.GetSelection();
                if (selection.Length > 0)
                {
                    var bounds = selection[0].GetBoundingRectangles();
                    if (bounds.Length > 0)
                    {
                        var rect = bounds[0];
                        return new System.Windows.Point(rect.Left, rect.Bottom);
                    }
                }
            }

            // Fallback to element bounding rect
            var elementRect = focused.Current.BoundingRectangle;
            if (!elementRect.IsEmpty)
            {
                return new System.Windows.Point(elementRect.Left, elementRect.Bottom);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "UIAutomation caret detection failed");
        }

        return null;
    }

    private System.Windows.Point? TryGetViaWin32()
    {
        try
        {
            var hwnd = NativeMethods.GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return null;

            uint threadId = NativeMethods.GetWindowThreadProcessId(hwnd, out _);
            uint currentThread = NativeMethods.GetCurrentThreadId();

            var info = new NativeMethods.GUITHREADINFO();
            info.cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.GUITHREADINFO>();

            if (NativeMethods.GetGUIThreadInfo(threadId, ref info) && info.hwndCaret != IntPtr.Zero)
            {
                var point = new NativeMethods.POINT
                {
                    X = info.rcCaret.Left,
                    Y = info.rcCaret.Bottom
                };

                NativeMethods.ClientToScreen(info.hwndCaret, ref point);
                return new System.Windows.Point(point.X, point.Y);
            }

            // Last fallback: use GetCaretPos with thread attachment
            NativeMethods.AttachThreadInput(currentThread, threadId, true);
            try
            {
                if (NativeMethods.GetCaretPos(out var caretPoint))
                {
                    NativeMethods.ClientToScreen(info.hwndFocus != IntPtr.Zero ? info.hwndFocus : hwnd, ref caretPoint);
                    return new System.Windows.Point(caretPoint.X, caretPoint.Y);
                }
            }
            finally
            {
                NativeMethods.AttachThreadInput(currentThread, threadId, false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Win32 caret detection failed");
        }

        return null;
    }
}
