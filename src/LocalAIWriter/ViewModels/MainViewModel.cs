using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalAIWriter.Core.Services;
using LocalAIWriter.Models;
using LocalAIWriter.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LocalAIWriter.ViewModels;

/// <summary>
/// Main ViewModel driving the system tray application.
/// Owns the global hook lifecycle, coordinates text interception,
/// and manages the overall application state.
/// </summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly GlobalHookService _hookService;
    private readonly TextInterceptor _textInterceptor;
    private readonly CaretPositionTracker _caretTracker;
    private readonly ModelInferenceService _inferenceService;
    private readonly ContextAwarenessService _contextService;
    private readonly NotificationService _notificationService;
    private readonly Services.ResilienceGuardian _resilience;
    private readonly AdaptiveLearningEngine _learningEngine;
    private readonly ThemeService _themeService;
    private readonly ILogger<MainViewModel> _logger;

    private AppSettings _settings;
    private readonly object _typingRequestLock = new();
    private CancellationTokenSource? _typingRequestCts;
    private long _typingRequestVersion;
    private string _lastRealtimeInputSignature = string.Empty;
    private string _lastRealtimeSuggestionSignature = string.Empty;
    private DateTime _lastRealtimeSuggestionAtUtc = DateTime.MinValue;

    private const int RealtimeMinChars = 12;
    private const int RealtimeMaxChars = 2000;
    private const float RealtimeMinConfidence = 0.84f;
    private static readonly TimeSpan RealtimeSuggestionCooldown = TimeSpan.FromMilliseconds(900);

    [ObservableProperty]
    private bool _isEnabled = true;

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private DegradationLevel _healthLevel = DegradationLevel.FullFunctionality;

    [ObservableProperty]
    private bool _isSuggestionPopupVisible;

    [ObservableProperty]
    private SuggestionPopupViewModel? _suggestionPopup;

    public MainViewModel(
        GlobalHookService hookService,
        TextInterceptor textInterceptor,
        CaretPositionTracker caretTracker,
        ModelInferenceService inferenceService,
        ContextAwarenessService contextService,
        NotificationService notificationService,
        Services.ResilienceGuardian resilience,
        AdaptiveLearningEngine learningEngine,
        ThemeService themeService,
        ILogger<MainViewModel> logger)
    {
        _hookService = hookService;
        _textInterceptor = textInterceptor;
        _caretTracker = caretTracker;
        _inferenceService = inferenceService;
        _contextService = contextService;
        _notificationService = notificationService;
        _resilience = resilience;
        _learningEngine = learningEngine;
        _themeService = themeService;
        _logger = logger;
        _settings = new AppSettings();

        // Wire events
        _hookService.HotkeyPressed += OnHotkeyPressed;
        _hookService.TypingPaused += OnTypingPaused;
        _resilience.DegradationChanged += (_, level) =>
        {
            HealthLevel = level;
            StatusText = level == DegradationLevel.FullFunctionality ? "Ready" : $"Degraded: {level}";
        };
    }

    /// <summary>Starts the application services.</summary>
    public void Initialize()
    {
        LoadSettings();
        _inferenceService.Start();
        _hookService.Start();
        _resilience.Start();
        _learningEngine.LoadProfile();

        _logger.LogInformation("MainViewModel initialized");
        StatusText = "Ready";
    }

    [RelayCommand]
    private void ToggleEnabled()
    {
        IsEnabled = !IsEnabled;
        _hookService.IsEnabled = IsEnabled;
        StatusText = IsEnabled ? "Ready" : "Paused";
    }

    [RelayCommand]
    private void ShowSettings()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var settingsWindow = new Views.SettingsWindow();
            if (App.Services != null)
            {
                settingsWindow.DataContext = App.Services.GetRequiredService<SettingsViewModel>();
            }
            settingsWindow.ShowDialog();
            // Reload settings after dialog closes
            LoadSettings();
        });
    }

    [RelayCommand]
    private void ExitApp()
    {
        _learningEngine.SaveProfileAsync().GetAwaiter().GetResult();
        SaveSettings();
        Application.Current.Shutdown();
    }

    public void Dispose()
    {
        lock (_typingRequestLock)
        {
            _typingRequestCts?.Cancel();
            _typingRequestCts?.Dispose();
            _typingRequestCts = null;
        }

        _hookService.HotkeyPressed -= OnHotkeyPressed;
        _hookService.TypingPaused -= OnTypingPaused;
        _hookService.Dispose();
        _inferenceService.Dispose();
        _resilience.Dispose();
        _themeService.Dispose();
    }

    #region Private Methods

    private async void OnHotkeyPressed(object? sender, EventArgs e)
    {
        if (!IsEnabled) return;

        try
        {
            // Check if current app should be excluded
            var context = _contextService.DetectContext();
            if (context.ProcessName != null &&
                _contextService.ShouldExclude(context.ProcessName, _settings.ExcludedApps))
            {
                _logger.LogDebug("Skipping excluded app: {App}", context.ProcessName);
                return;
            }

            StatusText = "Processing...";

            // Extract text
            var text = await _textInterceptor.ExtractTextAsync();
            if (string.IsNullOrWhiteSpace(text))
            {
                StatusText = "No text found";
                return;
            }

            // Run pipeline
            var correctionOptions = CorrectionOptions.FromSettings(_settings.AggressivenessLevel, _settings.SafetyMode);
            var result = await _inferenceService.ProcessTextAsync(text, correctionOptions);
            if (result == null || result.CorrectedText == text)
            {
                StatusText = "No corrections needed";
                _resilience.RecordSuccess("ModelInference");
                return;
            }

            var confidence = result.Corrections.Count > 0
                ? result.Corrections.Average(c => c.Confidence)
                : 0.8f;
            var threshold = _learningEngine.GetConfidenceThreshold(CorrectionType.Grammar);
            if (confidence < threshold)
            {
                _logger.LogDebug("Suggestion filtered by adaptive threshold (conf={Conf:F2}, threshold={Threshold:F2})",
                    confidence, threshold);
                StatusText = "No high-confidence corrections";
                return;
            }

            _resilience.RecordSuccess("ModelInference");

            // Show popup at caret
            var caretPos = _caretTracker.GetCaretPosition();
            ShowSuggestionPopup(result, caretPos);

            StatusText = "Ready";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hotkey processing failed");
            _resilience.RecordFailure("ModelInference", ex);
            StatusText = "Error — try again";
        }
    }

    private async void OnTypingPaused(object? sender, EventArgs e)
    {
        if (!IsEnabled || _settings.Mode != AutoCorrectMode.Automatic) return;

        var requestVersion = Interlocked.Increment(ref _typingRequestVersion);
        CancellationTokenSource requestCts;
        lock (_typingRequestLock)
        {
            _typingRequestCts?.Cancel();
            _typingRequestCts?.Dispose();
            _typingRequestCts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            requestCts = _typingRequestCts;
        }

        try
        {
            var text = await _textInterceptor.ExtractTextAsync(requestCts.Token);
            if (!ShouldProcessRealtimeText(text))
                return;

            var signature = BuildRealtimeSignature(text!);
            if (signature == _lastRealtimeInputSignature)
                return;
            _lastRealtimeInputSignature = signature;

            var correctionOptions = CorrectionOptions.FromSettings(_settings.AggressivenessLevel, _settings.SafetyMode);
            var result = await _inferenceService.ProcessTextAsync(
                text!,
                correctionOptions,
                priority: false,
                ct: requestCts.Token);

            if (requestCts.IsCancellationRequested || requestVersion != Volatile.Read(ref _typingRequestVersion))
                return;

            if (result == null ||
                result.CorrectedText == result.OriginalText ||
                result.SafetyDecision != SafetyDecision.Applied)
                return;

            var confidence = result.Corrections.Count > 0
                ? result.Corrections.Average(c => c.Confidence)
                : 0.8f;
            var threshold = Math.Max(_learningEngine.GetConfidenceThreshold(CorrectionType.Grammar), RealtimeMinConfidence);
            if (confidence < threshold)
                return;

            var suggestionSignature = BuildRealtimeSignature(result.OriginalText + "\n->\n" + result.CorrectedText);
            var now = DateTime.UtcNow;
            if (suggestionSignature == _lastRealtimeSuggestionSignature &&
                now - _lastRealtimeSuggestionAtUtc < RealtimeSuggestionCooldown)
                return;

            _lastRealtimeSuggestionSignature = suggestionSignature;
            _lastRealtimeSuggestionAtUtc = now;

            Application.Current.Dispatcher.Invoke(() =>
            {
                var caretPos = _caretTracker.GetCaretPosition();
                ShowSuggestionPopup(result, caretPos);
            });
        }
        catch (OperationCanceledException)
        {
            // Newer typing event replaced this request.
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Auto-suggest failed");
        }
    }

    private void ShowSuggestionPopup(Core.Services.PipelineResult result, Point? position)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            SuggestionPopup = new SuggestionPopupViewModel(
                result, position, _textInterceptor, _learningEngine, _logger);
            SuggestionPopup.Dismissed += (_, _) =>
            {
                IsSuggestionPopupVisible = false;
                SuggestionPopup = null;
            };
            IsSuggestionPopupVisible = true;
        });
    }

    private static bool ShouldProcessRealtimeText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var trimmed = text.Trim();
        if (trimmed.Length < RealtimeMinChars || trimmed.Length > RealtimeMaxChars)
            return false;

        // Realtime path should stay in prose domains, not code blocks.
        if (trimmed.Contains("```", StringComparison.Ordinal) ||
            trimmed.Contains('\t') ||
            trimmed.Contains("://", StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    private static string BuildRealtimeSignature(string text)
    {
        var normalized = string.Join(
            ' ',
            text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Trim()
            .ToLowerInvariant();

        return normalized.Length <= 256 ? normalized : normalized[..256];
    }

    private void LoadSettings()
    {
        try
        {
            var path = Core.Constants.SettingsFilePath;
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                _settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load settings");
            _settings = new AppSettings();
        }

        _hookService.SetTypingPauseDelay(_settings.TypingPauseMs);
        _themeService.ApplyTheme(_settings.Theme);
    }

    private void SaveSettings()
    {
        try
        {
            var dir = Path.GetDirectoryName(Core.Constants.SettingsFilePath);
            if (dir != null && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Core.Constants.SettingsFilePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save settings");
        }
    }

    #endregion
}
