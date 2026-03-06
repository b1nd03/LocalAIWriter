using System.Windows.Automation;
using System.Windows;
using Microsoft.Extensions.Logging;
using LocalAIWriter.Interop;

namespace LocalAIWriter.Services;

/// <summary>
/// Extracts and replaces text in the active application using
/// UIAutomation (primary) with clipboard fallback.
/// </summary>
public sealed class TextInterceptor
{
    private readonly ILogger<TextInterceptor> _logger;

    // Flag to prevent re-entrant SendInput from triggering the hook again
    private volatile bool _isSimulatingInput;

    public TextInterceptor(ILogger<TextInterceptor> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Extracts selected text (or current line) from the focused application.
    /// Returns null if no text could be extracted.
    /// </summary>
    public async Task<string?> ExtractTextAsync(CancellationToken ct = default)
    {
        // Method 1: UIAutomation (selection only — never grab full document)
        var text = TryExtractSelectedViaAutomation();
        if (text != null)
        {
            _logger.LogDebug("Extracted {Len} chars via UIAutomation selection", text.Length);
            return text;
        }

        // Method 2: Clipboard fallback (simulates Ctrl+C)
        text = await TryExtractViaClipboardAsync(ct);
        if (text != null)
        {
            _logger.LogDebug("Extracted {Len} chars via clipboard", text.Length);
            return text;
        }

        _logger.LogWarning("Failed to extract text from active application");
        return null;
    }

    /// <summary>
    /// Replaces selected text in the focused application with the corrected text.
    /// </summary>
    public async Task<bool> ReplaceTextAsync(string newText, CancellationToken ct = default)
    {
        // Method 1: UIAutomation (only for simple single-value fields)
        if (TryReplaceViaAutomation(newText))
        {
            _logger.LogDebug("Replaced text via UIAutomation");
            return true;
        }

        // Method 2: Clipboard paste
        if (await TryReplaceViaClipboardAsync(newText, ct))
        {
            _logger.LogDebug("Replaced text via clipboard");
            return true;
        }

        _logger.LogWarning("Failed to replace text in active application");
        return false;
    }

    #region UIAutomation Methods

    private string? TryExtractSelectedViaAutomation()
    {
        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused == null) return null;

            // Only extract the current selection — never the full document
            if (focused.TryGetCurrentPattern(TextPattern.Pattern, out var textObj))
            {
                var textPattern = (TextPattern)textObj;
                var selection = textPattern.GetSelection();
                if (selection.Length > 0)
                {
                    var selected = selection[0].GetText(-1);
                    if (!string.IsNullOrEmpty(selected))
                        return selected;
                }
                // No selection — do NOT fall back to full document; use clipboard instead
                return null;
            }

            // ValuePattern for simple text fields
            if (focused.TryGetCurrentPattern(ValuePattern.Pattern, out var valObj))
            {
                var valuePattern = (ValuePattern)valObj;
                var value = valuePattern.Current.Value;
                // Only return if it's reasonably sized (not a huge document field)
                if (!string.IsNullOrEmpty(value) && value.Length <= 5000)
                    return value;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "UIAutomation text extraction failed");
        }

        return null;
    }

    private bool TryReplaceViaAutomation(string newText)
    {
        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused == null) return false;

            // Only replace via ValuePattern for simple single-value controls
            // (not rich text — that would destroy formatting, images, undo history)
            if (focused.TryGetCurrentPattern(ValuePattern.Pattern, out var valObj))
            {
                var valuePattern = (ValuePattern)valObj;
                if (!valuePattern.Current.IsReadOnly)
                {
                    valuePattern.SetValue(newText);
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "UIAutomation text replacement failed");
        }

        return false;
    }

    #endregion

    #region Clipboard Methods

    private async Task<string?> TryExtractViaClipboardAsync(CancellationToken ct)
    {
        if (_isSimulatingInput) return null;

        try
        {
            // Save current clipboard
            IDataObject? savedClipboard = null;
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                try { savedClipboard = Clipboard.GetDataObject(); } catch { }
                // Clear clipboard so we can detect if Ctrl+C actually copies something new
                try { Clipboard.Clear(); } catch { }
            });

            // Simulate Ctrl+C
            _isSimulatingInput = true;
            try
            {
                SimulateKeyCombination(NativeMethods.VK_CONTROL, NativeMethods.VK_C);
            }
            finally
            {
                _isSimulatingInput = false;
            }

            // Poll for clipboard content instead of fixed delay (max 500ms, check every 20ms)
            string? text = null;
            var deadline = DateTime.UtcNow.AddMilliseconds(500);
            while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
            {
                await Task.Delay(20, ct);
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        if (Clipboard.ContainsText())
                            text = Clipboard.GetText();
                    }
                    catch { }
                });
                if (text != null) break;
            }

            // Restore clipboard
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    if (savedClipboard != null)
                        Clipboard.SetDataObject(savedClipboard, true);
                    else
                        Clipboard.Clear();
                }
                catch { }
            });

            return text;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Clipboard text extraction failed");
            return null;
        }
    }

    private async Task<bool> TryReplaceViaClipboardAsync(string newText, CancellationToken ct)
    {
        if (_isSimulatingInput) return false;

        try
        {
            // Save clipboard
            IDataObject? savedClipboard = null;
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                try { savedClipboard = Clipboard.GetDataObject(); } catch { }
                Clipboard.SetText(newText);
            });

            // Simulate Ctrl+V
            _isSimulatingInput = true;
            try
            {
                SimulateKeyCombination(NativeMethods.VK_CONTROL, NativeMethods.VK_V);
            }
            finally
            {
                _isSimulatingInput = false;
            }

            // Short delay to let the paste complete
            await Task.Delay(80, ct);

            // Restore clipboard
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    if (savedClipboard != null)
                        Clipboard.SetDataObject(savedClipboard, true);
                    else
                        Clipboard.Clear();
                }
                catch { }
            });

            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Clipboard text replacement failed");
            return false;
        }
    }

    private static void SimulateKeyCombination(ushort modifier, ushort key)
    {
        var inputs = new NativeMethods.INPUT[]
        {
            CreateKeyInput(modifier, false),
            CreateKeyInput(key, false),
            CreateKeyInput(key, true),
            CreateKeyInput(modifier, true),
        };
        NativeMethods.SendInput((uint)inputs.Length, inputs,
            System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.INPUT>());
    }

    private static NativeMethods.INPUT CreateKeyInput(ushort vk, bool keyUp)
    {
        return new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_KEYBOARD,
            u = new NativeMethods.InputUnion
            {
                ki = new NativeMethods.KEYBDINPUT
                {
                    wVk = vk,
                    dwFlags = keyUp ? NativeMethods.KEYEVENTF_KEYUP : NativeMethods.KEYEVENTF_KEYDOWN
                }
            }
        };
    }

    #endregion
}
