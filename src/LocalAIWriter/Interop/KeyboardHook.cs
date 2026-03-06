using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LocalAIWriter.Interop;

/// <summary>
/// Low-level keyboard hook wrapper. Installs a WH_KEYBOARD_LL hook
/// and raises events for key presses. Runs on a dedicated message pump thread.
/// </summary>
public sealed class KeyboardHook : IDisposable
{
    private IntPtr _hookId = IntPtr.Zero;
    private NativeMethods.LowLevelKeyboardProc? _proc;
    private bool _disposed;

    /// <summary>Raised when a key is pressed.</summary>
    public event EventHandler<KeyboardHookEventArgs>? KeyDown;

    /// <summary>Raised when a key is released.</summary>
    public event EventHandler<KeyboardHookEventArgs>? KeyUp;

    /// <summary>Gets whether the hook is currently installed.</summary>
    public bool IsInstalled => _hookId != IntPtr.Zero;

    /// <summary>
    /// Installs the low-level keyboard hook.
    /// </summary>
    /// <returns>True if hook was installed successfully.</returns>
    public bool Install()
    {
        if (_hookId != IntPtr.Zero)
            return true;

        _proc = HookCallback;
        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule!;
        _hookId = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_KEYBOARD_LL,
            _proc,
            NativeMethods.GetModuleHandle(curModule.ModuleName),
            0);

        return _hookId != IntPtr.Zero;
    }

    /// <summary>
    /// Uninstalls the keyboard hook.
    /// </summary>
    public void Uninstall()
    {
        if (_hookId != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            Uninstall();
            _disposed = true;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var hookStruct = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            int msg = wParam.ToInt32();

            var args = new KeyboardHookEventArgs(
                (int)hookStruct.vkCode,
                hookStruct.scanCode,
                hookStruct.flags);

            if (msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN)
            {
                KeyDown?.Invoke(this, args);
                if (args.Handled)
                    return (IntPtr)1;
            }
            else if (msg == NativeMethods.WM_KEYUP)
            {
                KeyUp?.Invoke(this, args);
            }
        }

        return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }
}

/// <summary>
/// Event args for keyboard hook events.
/// </summary>
public sealed class KeyboardHookEventArgs : EventArgs
{
    /// <summary>Virtual key code.</summary>
    public int VirtualKeyCode { get; }

    /// <summary>Scan code.</summary>
    public uint ScanCode { get; }

    /// <summary>Key flags.</summary>
    public uint Flags { get; }

    /// <summary>Set to true to swallow the key press.</summary>
    public bool Handled { get; set; }

    public KeyboardHookEventArgs(int vkCode, uint scanCode, uint flags)
    {
        VirtualKeyCode = vkCode;
        ScanCode = scanCode;
        Flags = flags;
    }
}
