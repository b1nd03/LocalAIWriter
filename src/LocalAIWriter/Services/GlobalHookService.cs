using LocalAIWriter.Interop;
using Microsoft.Extensions.Logging;

namespace LocalAIWriter.Services;

/// <summary>
/// Manages the global low-level keyboard hook, detects hotkey combinations,
/// and monitors typing pauses for auto-suggest mode.
/// </summary>
public sealed class GlobalHookService : IDisposable
{
    private readonly KeyboardHook _hook;
    private readonly ILogger<GlobalHookService> _logger;
    private readonly System.Timers.Timer _typingPauseTimer;

    // volatile ensures cross-thread visibility without a full lock
    private volatile bool _ctrlPressed;
    private volatile bool _isEnabled = true;
    private int _typingPauseMs = Core.Constants.DefaultTypingPauseMs;

    // Configurable hotkey (defaults to Ctrl+Space)
    private ushort _hotkeyModifier = NativeMethods.VK_CONTROL;
    private ushort _hotkeyKey = NativeMethods.VK_SPACE;

    /// <summary>Raised when the improve hotkey is pressed (default: Ctrl+Space).</summary>
    public event EventHandler? HotkeyPressed;

    /// <summary>Raised when user pauses typing for the configured duration.</summary>
    public event EventHandler? TypingPaused;

    /// <summary>Gets or sets whether the hook is actively monitoring.</summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            _isEnabled = value;
            if (!value)
                _typingPauseTimer.Stop();
            _logger.LogInformation("Global hook {State}", value ? "enabled" : "disabled");
        }
    }

    public GlobalHookService(ILogger<GlobalHookService> logger)
    {
        _logger = logger;
        _hook = new KeyboardHook();
        _hook.KeyDown += OnKeyDown;
        _hook.KeyUp += OnKeyUp;

        _typingPauseTimer = new System.Timers.Timer(_typingPauseMs);
        _typingPauseTimer.AutoReset = false;
        _typingPauseTimer.Elapsed += OnTypingPauseElapsed;
    }

    /// <summary>Installs the global keyboard hook.</summary>
    public bool Start()
    {
        bool result = _hook.Install();
        if (result)
            _logger.LogInformation("Global keyboard hook installed");
        else
            _logger.LogError("Failed to install global keyboard hook — hotkey will not work");
        return result;
    }

    /// <summary>Uninstalls the global keyboard hook.</summary>
    public void Stop()
    {
        _isEnabled = false;
        _hook.Uninstall();
        _typingPauseTimer.Stop();
        _logger.LogInformation("Global keyboard hook uninstalled");
    }

    /// <summary>Sets the typing pause delay for auto-suggest.</summary>
    public void SetTypingPauseDelay(int milliseconds)
    {
        _typingPauseMs = milliseconds;
        _typingPauseTimer.Interval = milliseconds;
    }

    /// <summary>Sets the hotkey combination.</summary>
    public void SetHotkey(ushort modifierVk, ushort keyVk)
    {
        _hotkeyModifier = modifierVk;
        _hotkeyKey = keyVk;
    }

    public void Dispose()
    {
        _hook.KeyDown -= OnKeyDown;
        _hook.KeyUp -= OnKeyUp;
        _typingPauseTimer.Elapsed -= OnTypingPauseElapsed;
        _hook.Dispose();
        _typingPauseTimer.Dispose();
    }

    private void OnTypingPauseElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        try
        {
            TypingPaused?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TypingPaused event handler threw an exception");
        }
    }

    private void OnKeyDown(object? sender, KeyboardHookEventArgs e)
    {
        if (!_isEnabled) return;

        // Track Ctrl modifier — use GetKeyState to handle left/right Ctrl correctly
        if (e.VirtualKeyCode == NativeMethods.VK_CONTROL ||
            e.VirtualKeyCode == NativeMethods.VK_LCONTROL ||
            e.VirtualKeyCode == NativeMethods.VK_RCONTROL)
        {
            _ctrlPressed = true;
            return;
        }

        // Detect configured hotkey
        if (_ctrlPressed && e.VirtualKeyCode == _hotkeyKey)
        {
            _logger.LogDebug("Hotkey detected: Ctrl+{Key}", _hotkeyKey);
            try
            {
                HotkeyPressed?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "HotkeyPressed event handler threw an exception");
            }
            e.Handled = true;
            return;
        }

        // Reset typing pause timer on any printable key press
        // Extended range to include common non-ASCII typed characters
        if ((e.VirtualKeyCode >= 0x20 && e.VirtualKeyCode <= 0xFE) &&
            e.VirtualKeyCode != NativeMethods.VK_CONTROL &&
            e.VirtualKeyCode != NativeMethods.VK_LCONTROL &&
            e.VirtualKeyCode != NativeMethods.VK_RCONTROL)
        {
            _typingPauseTimer.Stop();
            _typingPauseTimer.Start();
        }
    }

    private void OnKeyUp(object? sender, KeyboardHookEventArgs e)
    {
        if (e.VirtualKeyCode == NativeMethods.VK_CONTROL ||
            e.VirtualKeyCode == NativeMethods.VK_LCONTROL ||
            e.VirtualKeyCode == NativeMethods.VK_RCONTROL)
        {
            // Only clear if both left and right Ctrl are released
            if ((NativeMethods.GetKeyState(NativeMethods.VK_CONTROL) & 0x8000) == 0)
                _ctrlPressed = false;
        }
    }
}
