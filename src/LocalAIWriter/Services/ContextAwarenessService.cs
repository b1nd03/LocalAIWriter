using System.Diagnostics;
using LocalAIWriter.Interop;
using LocalAIWriter.Models;
using Microsoft.Extensions.Logging;

namespace LocalAIWriter.Services;

/// <summary>
/// Detects the writing context from the active application to provide
/// application-aware, genre-aware corrections.
/// </summary>
public sealed class ContextAwarenessService
{
    private readonly ILogger<ContextAwarenessService> _logger;

    private static readonly Dictionary<string, ApplicationType> KnownApps = new(StringComparer.OrdinalIgnoreCase)
    {
        ["notepad"] = ApplicationType.TextEditor,
        ["wordpad"] = ApplicationType.TextEditor,
        ["winword"] = ApplicationType.DocumentEditor,
        ["excel"] = ApplicationType.DocumentEditor,
        ["powerpnt"] = ApplicationType.DocumentEditor,
        ["code"] = ApplicationType.CodeEditor,
        ["devenv"] = ApplicationType.CodeEditor,
        ["idea64"] = ApplicationType.CodeEditor,
        ["sublime_text"] = ApplicationType.CodeEditor,
        ["outlook"] = ApplicationType.EmailClient,
        ["thunderbird"] = ApplicationType.EmailClient,
        ["chrome"] = ApplicationType.Browser,
        ["msedge"] = ApplicationType.Browser,
        ["firefox"] = ApplicationType.Browser,
        ["opera"] = ApplicationType.Browser,
        ["slack"] = ApplicationType.ChatApplication,
        ["teams"] = ApplicationType.ChatApplication,
        ["discord"] = ApplicationType.ChatApplication,
        ["telegram"] = ApplicationType.ChatApplication,
        ["whatsapp"] = ApplicationType.ChatApplication,
        ["windowsterminal"] = ApplicationType.Terminal,
        ["cmd"] = ApplicationType.Terminal,
        ["powershell"] = ApplicationType.Terminal,
    };

    public ContextAwarenessService(ILogger<ContextAwarenessService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Detects the writing context from the currently focused application.
    /// </summary>
    public WritingContext DetectContext()
    {
        try
        {
            var hwnd = NativeMethods.GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
                return CreateDefault();

            NativeMethods.GetWindowThreadProcessId(hwnd, out uint processId);
            var process = Process.GetProcessById((int)processId);
            var processName = process.ProcessName.ToLowerInvariant();

            var appType = KnownApps.TryGetValue(processName, out var type)
                ? type : ApplicationType.Unknown;

            var formality = appType switch
            {
                ApplicationType.EmailClient => FormalityLevel.Formal,
                ApplicationType.DocumentEditor => FormalityLevel.Formal,
                ApplicationType.ChatApplication => FormalityLevel.Casual,
                ApplicationType.CodeEditor => FormalityLevel.Neutral,
                ApplicationType.Terminal => FormalityLevel.Neutral,
                _ => FormalityLevel.Neutral
            };

            return new WritingContext
            {
                AppType = appType,
                Formality = formality,
                IsCodeMixed = appType == ApplicationType.CodeEditor || appType == ApplicationType.Terminal,
                ProcessName = processName
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Context detection failed");
            return CreateDefault();
        }
    }

    /// <summary>Checks if the current app should be excluded from corrections.</summary>
    public bool ShouldExclude(string processName, IReadOnlyList<string> excludedApps)
    {
        return excludedApps.Any(app =>
            processName.Contains(app, StringComparison.OrdinalIgnoreCase));
    }

    private static WritingContext CreateDefault() => new()
    {
        AppType = ApplicationType.Unknown,
        Formality = FormalityLevel.Neutral,
        IsCodeMixed = false
    };
}
