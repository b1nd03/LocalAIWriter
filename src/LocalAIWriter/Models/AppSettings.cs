using System.Text.Json.Serialization;
using LocalAIWriter.Core.Services;

namespace LocalAIWriter.Models;

/// <summary>
/// Application settings persisted to %AppData% as JSON.
/// </summary>
public sealed class AppSettings
{
    public bool IsEnabled { get; set; } = true;
    public int AggressivenessLevel { get; set; } = 1;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public CorrectionSafetyMode SafetyMode { get; set; } = CorrectionSafetyMode.Conservative;

    public string OllamaEndpoint { get; set; } = "http://localhost:11434";
    public string OllamaModel { get; set; } = "gemma2:2b";

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AutoCorrectMode Mode { get; set; } = AutoCorrectMode.Manual;

    public string HotkeyModifiers { get; set; } = "Ctrl";
    public string HotkeyKey { get; set; } = "Space";
    public bool ShowImproveButton { get; set; } = true;
    public bool StartWithWindows { get; set; } = false;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AppTheme Theme { get; set; } = AppTheme.System;

    public int PopupTimeoutMs { get; set; } = 5000;
    public int MaxSuggestions { get; set; } = 5;
    public int TypingPauseMs { get; set; } = 1500;
    public List<string> ExcludedApps { get; set; } = new();
    public bool ReducedMotion { get; set; }
    public bool HighContrast { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public WritingStyle WritingStyle { get; set; } = WritingStyle.General;

    public bool IsFirstLaunch { get; set; } = true;
    public bool UseGpuAcceleration { get; set; }
}

public enum AutoCorrectMode { Manual, Automatic, HighlightOnly }
public enum AppTheme { System, Light, Dark }
public enum WritingStyle { General, Academic, Business, Creative, Technical, Custom }
