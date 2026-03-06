using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using LocalAIWriter.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LocalAIWriter;

public partial class MainWindow : Window
{
    private RuleBasedEngine? _ruleEngine;
    private OllamaService? _ollama;
    private ILogger<MainWindow>? _logger;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public INPUTUNION union;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12; // Alt
    private const int VK_G = 0x47;

    private IntPtr _hookId = IntPtr.Zero;
    private LowLevelKeyboardProc? _hookProc;
    private bool _isProcessingHotkey;

    private System.Windows.Forms.NotifyIcon? _trayIcon;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnWindowLoaded;
        Closed += OnWindowClosed;
    }

    private void InitTrayIcon()
    {
        try
        {
            _trayIcon = new System.Windows.Forms.NotifyIcon
            {
                Visible = true,
                Text = "LocalAI Writer — by b1nd03"
            };
            var exeDir = AppDomain.CurrentDomain.BaseDirectory;
            var icoPath = Path.Combine(exeDir, "app_icon.ico");
            if (File.Exists(icoPath))
                _trayIcon.Icon = new System.Drawing.Icon(icoPath);
            else
                _trayIcon.Icon = System.Drawing.SystemIcons.Application;
        }
        catch { }
    }

    private void ShowTrayNotification(string title, string message,
        System.Windows.Forms.ToolTipIcon icon = System.Windows.Forms.ToolTipIcon.Info)
    {
        try
        {
            _trayIcon?.ShowBalloonTip(3000, title, message, icon);
        }
        catch { }
    }

    private async void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var exeDir = AppDomain.CurrentDomain.BaseDirectory;
            var icoPath = Path.Combine(exeDir, "app_icon.ico");
            var pngPath = Path.Combine(exeDir, "app_icon.png");
            if (File.Exists(icoPath)) Icon = new BitmapImage(new Uri(icoPath, UriKind.Absolute));
            else if (File.Exists(pngPath)) Icon = new BitmapImage(new Uri(pngPath, UriKind.Absolute));
        }
        catch { }

        try
        {
            _ruleEngine = App.Services?.GetService<RuleBasedEngine>() ?? new RuleBasedEngine();
            _ollama = App.Services?.GetService<OllamaService>() ?? new OllamaService();
            _logger = App.Services?.GetService<ILogger<MainWindow>>();
        }
        catch
        {
            _ruleEngine = new RuleBasedEngine();
            _ollama = new OllamaService();
        }

        InitTrayIcon();
        InstallKeyboardHook();

        try
        {
            var activeCfg = _ollama.GetActiveConfiguration();
            ModelLabel.Text = $"Model: {activeCfg.Model}";
            var (running, modelReady, _) = await _ollama.CheckStatusAsync();
            StatusLabel.Text = running && modelReady
                ? "✅ Ready! Select text → Ctrl+Alt+G → grammar corrected"
                : running ? $"⚠️ Run: ollama pull {activeCfg.Model}" : $"⚠️ Start Ollama ({activeCfg.Endpoint})";
        }
        catch { StatusLabel.Text = "Ready — keyboard hook active."; }
    }

    private void InstallKeyboardHook()
    {
        _hookProc = HookCallback;
        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc, IntPtr.Zero, 0);

        if (_hookId == IntPtr.Zero)
        {
            int err = Marshal.GetLastWin32Error();
            StatusLabel.Text = $"⚠️ Hook failed (error {err})";
        }
        else
        {
            StatusLabel.Text = "✅ Ctrl+Alt+G active — select text in any app!";
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam == (IntPtr)WM_KEYDOWN)
        {
            var hookStruct = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);

            if (hookStruct.vkCode == VK_G)
            {
                bool ctrlHeld = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
                bool altHeld = (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;

                if (ctrlHeld && altHeld && !_isProcessingHotkey)
                {
                    var targetWnd = GetForegroundWindow();
                    Dispatcher.BeginInvoke(() => _ = OnGlobalHotkeyAsync(targetWnd));
                    return (IntPtr)1;
                }
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    [DllImport("user32.dll")]
    private static extern bool IsZoomed(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    private static string _debugLog = "";
    private static void DebugLog(string msg)
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] {msg}";
        _debugLog += line + "\n";
        try
        {
            var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_log.txt");
            File.AppendAllText(logPath, line + Environment.NewLine);
        }
        catch { }
    }

    private static bool FocusWindowSafely(IntPtr targetHwnd)
    {
        DebugLog($"FocusWindowSafely: hwnd=0x{targetHwnd:X}");

        var currentThread = GetCurrentThreadId();
        var targetThread = GetWindowThreadProcessId(targetHwnd, out var pid);
        DebugLog($"  currentThread={currentThread}, targetThread={targetThread}, pid={pid}");

        bool attached = false;
        if (currentThread != targetThread)
        {
            attached = AttachThreadInput(currentThread, targetThread, true);
            DebugLog($"  AttachThreadInput={attached}");
        }

        bool isMin = IsIconic(targetHwnd);
        bool isMax = IsZoomed(targetHwnd);
        int swFlag = isMin ? 9 : isMax ? 3 : 5;
        DebugLog($"  IsIconic={isMin}, IsZoomed={isMax}, swFlag={swFlag}");

        bool sw = ShowWindow(targetHwnd, swFlag);
        bool btt = BringWindowToTop(targetHwnd);
        bool sfw = SetForegroundWindow(targetHwnd);
        DebugLog($"  ShowWindow={sw}, BringWindowToTop={btt}, SetForegroundWindow={sfw}");

        var fgNow = GetForegroundWindow();
        DebugLog($"  ForegroundWindow after: 0x{fgNow:X} (target=0x{targetHwnd:X}, match={fgNow == targetHwnd})");

        if (attached)
            AttachThreadInput(currentThread, targetThread, false);

        return sfw;
    }

    private async Task OnGlobalHotkeyAsync(IntPtr targetWindow)
    {
        if (_isProcessingHotkey) return;
        _isProcessingHotkey = true;
        _debugLog = "";

        DebugLog($"=== HOTKEY START === targetWindow=0x{targetWindow:X}");

        try
        {
            DebugLog("Step 1: Waiting for Ctrl+Alt release...");
            await Task.Run(() =>
            {
                int timeout = 3000;
                while (timeout > 0)
                {
                    bool ctrlDown = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
                    bool altDown = (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;
                    if (!ctrlDown && !altDown) break;
                    Thread.Sleep(50);
                    timeout -= 50;
                }
            });
            await Task.Delay(200);
            DebugLog("Step 1: Keys released OK");

            // 2. Copy via STA thread
            DebugLog("Step 2: Starting copy...");
            string selectedText = "";
            string copyError = "";

            await Task.Run(() =>
            {
                var thread = new Thread(() =>
                {
                    try
                    {
                        DebugLog("  Copy: Clearing clipboard");
                        try { System.Windows.Forms.Clipboard.Clear(); } catch (Exception ex) { DebugLog($"  Copy: Clear failed: {ex.Message}"); }
                        Thread.Sleep(50);

                        DebugLog("  Copy: Focusing target window");
                        bool focused = FocusWindowSafely(targetWindow);
                        DebugLog($"  Copy: FocusWindowSafely returned {focused}");
                        Thread.Sleep(300);

                        DebugLog("  Copy: Sending Ctrl+C via SendKeys");
                        System.Windows.Forms.SendKeys.SendWait("^c");
                        Thread.Sleep(500);

                        bool hasText = System.Windows.Forms.Clipboard.ContainsText();
                        DebugLog($"  Copy: Clipboard.ContainsText={hasText}");

                        if (hasText)
                        {
                            selectedText = System.Windows.Forms.Clipboard.GetText();
                            DebugLog($"  Copy: Got text ({selectedText.Length} chars): '{selectedText.Substring(0, Math.Min(50, selectedText.Length))}'");
                        }
                        else
                        {
                            DebugLog("  Copy: NO TEXT in clipboard after Ctrl+C");
                        }
                    }
                    catch (Exception ex)
                    {
                        copyError = ex.Message;
                        DebugLog($"  Copy: EXCEPTION: {ex}");
                    }
                });
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                thread.Join(8000);
            });

            if (string.IsNullOrWhiteSpace(selectedText))
            {
                DebugLog($"Step 2: FAILED — no text, error='{copyError}'");
                ShowTrayNotification("LocalAI Writer", "⚠️ No text selected",
                    System.Windows.Forms.ToolTipIcon.Warning);
                Dispatcher.Invoke(() => StatusLabel.Text = $"⚠️ No text — debug: {_debugLog.Substring(Math.Max(0, _debugLog.Length - 200))}");
                return;
            }

            DebugLog($"Step 2: SUCCESS — got '{selectedText.Substring(0, Math.Min(30, selectedText.Length))}...'");
            ShowTrayNotification("LocalAI Writer", "🧠 Correcting...", System.Windows.Forms.ToolTipIcon.Info);
            Dispatcher.Invoke(() => StatusLabel.Text = $"🧠 AI correcting {selectedText.Length} chars...");

            DebugLog("Step 3: AI correction...");
            var result = await (_ollama ?? new OllamaService()).CorrectGrammarAsync(selectedText);
            DebugLog($"Step 3: Success={result.Success}, Changed={result.CorrectedText != selectedText}, Msg={result.Message}");

            if (result.Success && result.CorrectedText != selectedText)
            {
                DebugLog("Step 4: Starting paste...");
                string pasteError = "";

                await Task.Run(() =>
                {
                    var thread = new Thread(() =>
                    {
                        try
                        {
                            DebugLog("  Paste: Setting clipboard text");
                            System.Windows.Forms.Clipboard.SetText(result.CorrectedText);
                            Thread.Sleep(100);

                            bool clipOk = System.Windows.Forms.Clipboard.ContainsText();
                            string clipText = clipOk ? System.Windows.Forms.Clipboard.GetText() : "";
                            DebugLog($"  Paste: Clipboard set OK={clipOk}, matches={clipText == result.CorrectedText}");

                            DebugLog("  Paste: Focusing target window");
                            bool focused = FocusWindowSafely(targetWindow);
                            DebugLog($"  Paste: FocusWindowSafely returned {focused}");
                            Thread.Sleep(300);

                            var fg = GetForegroundWindow();
                            DebugLog($"  Paste: Current foreground=0x{fg:X}, target=0x{targetWindow:X}, match={fg == targetWindow}");

                            DebugLog("  Paste: Sending Ctrl+V via SendKeys");
                            System.Windows.Forms.SendKeys.SendWait("^v");
                            DebugLog("  Paste: SendKeys returned");
                            Thread.Sleep(300);
                        }
                        catch (Exception ex)
                        {
                            pasteError = ex.Message;
                            DebugLog($"  Paste: EXCEPTION: {ex}");
                        }
                    });
                    thread.SetApartmentState(ApartmentState.STA);
                    thread.Start();
                    thread.Join(8000);
                });

                DebugLog($"Step 4: Paste completed, error='{pasteError}'");

                ShowTrayNotification("LocalAI Writer", "✅ Grammar corrected!",
                    System.Windows.Forms.ToolTipIcon.Info);

                Dispatcher.Invoke(() =>
                {
                    InputBox.Text = selectedText;
                    OutputBox.Text = result.CorrectedText;
                    SetOutputColor(true);
                    StatusLabel.Text = $"✅ Corrected! See debug_log.txt for details.";
                    CorrectionsLabel.Text = result.Message;
                });
            }
            else
            {
                var msg = result.Success ? "✅ Already correct!" : $"⚠️ {result.Message}";
                DebugLog($"Step 4: No paste needed — {msg}");
                ShowTrayNotification("LocalAI Writer", msg,
                    result.Success ? System.Windows.Forms.ToolTipIcon.Info : System.Windows.Forms.ToolTipIcon.Warning);
                Dispatcher.Invoke(() => StatusLabel.Text = msg);
            }
        }
        catch (Exception ex)
        {
            DebugLog($"EXCEPTION: {ex}");
            ShowTrayNotification("LocalAI Writer", $"Error: {ex.Message}",
                System.Windows.Forms.ToolTipIcon.Error);
            Dispatcher.Invoke(() => StatusLabel.Text = $"Error: {ex.Message}");
        }
        finally
        {
            DebugLog("=== HOTKEY END ===");
            _isProcessingHotkey = false;
        }
    }




    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (_hookId != IntPtr.Zero) UnhookWindowsHookEx(_hookId);
        if (_trayIcon != null) { _trayIcon.Visible = false; _trayIcon.Dispose(); }
    }

    // ──── BUTTON HANDLERS ────

    private async void OnAiImproveClicked(object sender, RoutedEventArgs e)
    {
        var inputText = InputBox.Text;
        if (string.IsNullOrWhiteSpace(inputText))
        { OutputBox.Text = ""; StatusLabel.Text = "Enter text first."; return; }

        AiImproveButton.IsEnabled = false;
        StatusLabel.Text = "🧠 AI processing..."; CorrectionsLabel.Text = ""; LatencyLabel.Text = ""; OutputBox.Text = "";
        try
        {
            var sw = Stopwatch.StartNew();
            var ai = await (_ollama ?? new OllamaService()).CorrectGrammarAsync(inputText);
            sw.Stop();
            if (ai.Success)
            {
                OutputBox.Text = ai.CorrectedText; SetOutputColor(ai.CorrectedText != inputText);
                bool changed = ai.CorrectedText != inputText;
                CorrectionsLabel.Text = changed ? "✅ AI corrected" : "No changes";
                StatusLabel.Text = changed ? $"Done — AI in {sw.ElapsedMilliseconds}ms" : $"Good! ({sw.ElapsedMilliseconds}ms)";
            }
            else
            {
                OutputBox.Text = inputText;
                StatusLabel.Text = $"⚠️ {ai.Message}";
            }
            LatencyLabel.Text = $"{sw.ElapsedMilliseconds}ms";
        }
        catch (Exception ex) { OutputBox.Text = inputText; StatusLabel.Text = $"Error: {ex.Message}"; }
        finally { AiImproveButton.IsEnabled = true; }
    }

    private void SetOutputColor(bool changed) =>
        OutputBox.Foreground = new System.Windows.Media.SolidColorBrush(
            changed ? System.Windows.Media.Color.FromRgb(5, 150, 105)
                    : System.Windows.Media.Color.FromRgb(55, 65, 81));

    private void OnCopyClicked(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(OutputBox.Text))
            try { Clipboard.SetText(OutputBox.Text); StatusLabel.Text = "📋 Copied!"; } catch { }
    }

    private void OnSettingsClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            var s = new Views.SettingsWindow();
            if (App.Services != null) s.DataContext = App.Services.GetRequiredService<ViewModels.SettingsViewModel>();
            s.Owner = this; s.ShowDialog();

            // Refresh ModelLabel after settings window closes
            if (_ollama != null)
            {
                var cfg = _ollama.GetActiveConfiguration();
                ModelLabel.Text = $"Model: {cfg.Model}";
            }
        }
        catch (Exception ex) { MessageBox.Show($"Error: {ex.Message}"); }
    }
}
