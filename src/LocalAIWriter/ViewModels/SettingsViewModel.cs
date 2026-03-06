using System.Text.Json;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalAIWriter.Core.Services;
using LocalAIWriter.Models;
using LocalAIWriter.Services;
using Microsoft.Extensions.Logging;

namespace LocalAIWriter.ViewModels;

/// <summary>
/// ViewModel for the Settings window. Binds to all configurable options.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ThemeService _themeService;
    private readonly OllamaService _ollamaService;
    private readonly ILogger<SettingsViewModel> _logger;

    [ObservableProperty] private bool _isEnabled = true;
    [ObservableProperty] private int _aggressivenessLevel = 1;
    [ObservableProperty] private CorrectionSafetyMode _safetyMode = CorrectionSafetyMode.Conservative;
    [ObservableProperty] private AutoCorrectMode _mode = AutoCorrectMode.Manual;
    [ObservableProperty] private string _hotkeyModifiers = "Ctrl";
    [ObservableProperty] private string _hotkeyKey = "Space";
    [ObservableProperty] private bool _showImproveButton = true;
    [ObservableProperty] private bool _startWithWindows;
    [ObservableProperty] private AppTheme _theme = AppTheme.System;
    [ObservableProperty] private int _popupTimeoutMs = 5000;
    [ObservableProperty] private int _maxSuggestions = 5;
    [ObservableProperty] private int _typingPauseMs = 1500;
    [ObservableProperty] private bool _reducedMotion;
    [ObservableProperty] private bool _highContrast;
    [ObservableProperty] private WritingStyle _writingStyle = WritingStyle.General;
    [ObservableProperty] private string _ollamaEndpoint = "http://localhost:11434";
    [ObservableProperty] private string _ollamaModel = "gemma2:2b";
    [ObservableProperty] private string _activeModelLabel = "gemma2:2b @ http://localhost:11434";
    [ObservableProperty] private bool _isRefreshingModels;

    public ObservableCollection<string> AvailableOllamaModels { get; } = new();

    public string AggressivenessLabel => AggressivenessLevel switch
    {
        0 => "Light",
        1 => "Balanced",
        _ => "Thorough"
    };

    partial void OnAggressivenessLevelChanged(int value) => OnPropertyChanged(nameof(AggressivenessLabel));

    // Status page
    [ObservableProperty] private int _totalCorrections;
    [ObservableProperty] private float _acceptanceRate;
    [ObservableProperty] private string _modelStatus = "Not loaded";
    [ObservableProperty] private string _memoryUsage = "0 MB";

    public SettingsViewModel(
        ThemeService themeService,
        OllamaService ollamaService,
        ILogger<SettingsViewModel> logger)
    {
        _themeService = themeService;
        _ollamaService = ollamaService;
        _logger = logger;
        LoadSettings();
        _ = RefreshModelsAsync();
        UpdateStatus();
    }

    partial void OnThemeChanged(AppTheme value)
    {
        _themeService.ApplyTheme(value);
    }

    partial void OnHighContrastChanged(bool value)
    {
        if (value)
            _themeService.ApplyTheme(AppTheme.Dark); // High contrast uses dark base
        else
            _themeService.ApplyTheme(Theme);
    }

    private void UpdateStatus()
    {
        var proc = System.Diagnostics.Process.GetCurrentProcess();
        MemoryUsage = $"{proc.WorkingSet64 / (1024 * 1024)} MB";
    }

    partial void OnOllamaEndpointChanged(string value) => ActiveModelLabel = $"{OllamaModel} @ {value}";
    partial void OnOllamaModelChanged(string value) => ActiveModelLabel = $"{value} @ {OllamaEndpoint}";

    [RelayCommand]
    private void Save()
    {
        var settings = new AppSettings
        {
            IsEnabled = IsEnabled,
            AggressivenessLevel = AggressivenessLevel,
            SafetyMode = SafetyMode,
            Mode = Mode,
            HotkeyModifiers = HotkeyModifiers,
            HotkeyKey = HotkeyKey,
            ShowImproveButton = ShowImproveButton,
            StartWithWindows = StartWithWindows,
            Theme = Theme,
            PopupTimeoutMs = PopupTimeoutMs,
            MaxSuggestions = MaxSuggestions,
            TypingPauseMs = TypingPauseMs,
            ReducedMotion = ReducedMotion,
            HighContrast = HighContrast,
            WritingStyle = WritingStyle,
            OllamaEndpoint = string.IsNullOrWhiteSpace(OllamaEndpoint) ? "http://localhost:11434" : OllamaEndpoint.Trim(),
            OllamaModel = string.IsNullOrWhiteSpace(OllamaModel) ? "gemma2:2b" : OllamaModel.Trim()
        };

        try
        {
            var dir = System.IO.Path.GetDirectoryName(Core.Constants.SettingsFilePath);
            if (dir != null && !System.IO.Directory.Exists(dir))
                System.IO.Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            System.IO.File.WriteAllText(Core.Constants.SettingsFilePath, json);
            _logger.LogInformation("Settings saved");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save settings");
        }
    }

    private void LoadSettings()
    {
        try
        {
            if (System.IO.File.Exists(Core.Constants.SettingsFilePath))
            {
                var json = System.IO.File.ReadAllText(Core.Constants.SettingsFilePath);
                var s = JsonSerializer.Deserialize<AppSettings>(json);
                if (s != null)
                {
                    IsEnabled = s.IsEnabled;
                    AggressivenessLevel = s.AggressivenessLevel;
                    SafetyMode = s.SafetyMode;
                    Mode = s.Mode;
                    HotkeyModifiers = s.HotkeyModifiers;
                    HotkeyKey = s.HotkeyKey;
                    ShowImproveButton = s.ShowImproveButton;
                    StartWithWindows = s.StartWithWindows;
                    Theme = s.Theme;
                    PopupTimeoutMs = s.PopupTimeoutMs;
                    MaxSuggestions = s.MaxSuggestions;
                    TypingPauseMs = s.TypingPauseMs;
                    ReducedMotion = s.ReducedMotion;
                    HighContrast = s.HighContrast;
                    WritingStyle = s.WritingStyle;
                    OllamaEndpoint = string.IsNullOrWhiteSpace(s.OllamaEndpoint) ? "http://localhost:11434" : s.OllamaEndpoint;
                    OllamaModel = string.IsNullOrWhiteSpace(s.OllamaModel) ? "gemma2:2b" : s.OllamaModel;
                    ActiveModelLabel = $"{OllamaModel} @ {OllamaEndpoint}";
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load settings");
        }
    }

    [RelayCommand]
    private async Task RefreshModelsAsync()
    {
        if (IsRefreshingModels)
            return;

        IsRefreshingModels = true;
        try
        {
            var result = await _ollamaService.ListAvailableModelsAsync(OllamaEndpoint, OllamaModel);
            ModelStatus = result.Status;

            AvailableOllamaModels.Clear();
            foreach (var model in result.Models)
                AvailableOllamaModels.Add(model);

            if (result.Success)
            {
                OllamaEndpoint = result.Endpoint;

                bool selectedModelFound = AvailableOllamaModels
                    .Any(m => string.Equals(m, OllamaModel, StringComparison.OrdinalIgnoreCase));

                if (!selectedModelFound && AvailableOllamaModels.Count > 0)
                {
                    OllamaModel = AvailableOllamaModels[0];
                    selectedModelFound = true;
                }

                if (!selectedModelFound)
                {
                    ModelStatus = result.Models.Count > 0
                        ? $"{result.Status} (selected model not on endpoint)"
                        : result.Status;
                }
            }

            ActiveModelLabel = $"{OllamaModel} @ {result.Endpoint}";
        }
        catch (Exception ex)
        {
            ModelStatus = "Failed to load models";
            _logger.LogWarning(ex, "Failed to refresh Ollama model list");
        }
        finally
        {
            IsRefreshingModels = false;
        }
    }
}
