namespace LocalAIWriter.Core;

/// <summary>
/// Application-wide constants for model paths, configuration defaults, and system limits.
/// </summary>
public static class Constants
{
    #region Application Info

    public const string AppName = "LocalAI Writer";
    public const string AppVersion = "1.0.0";
    public const string AppDataFolderName = "LocalAIWriter";

    #endregion

    #region Model Paths

    public const string DefaultModelFileName = "grammar_corrector.onnx";
    public const string DefaultModelHashFileName = "grammar_corrector.onnx.sha256";
    public const string DefaultTokenizerFileName = "tokenizer.model";
    public const string ModelCardFileName = "model_card.json";

    #endregion

    #region Inference Defaults

    public const int MaxInputTokens = 64;
    public const int MaxOutputTokens = 64;
    public const int InferenceTimeoutMs = 5000;
    public const int ModelLoadTimeoutMs = 10_000;
    public const int PredictionCacheMaxSize = 200;
    public const int PredictionCacheMaxSizeMb = 5;
    public const int InferenceChannelCapacity = 3;

    #endregion

    #region Performance Targets

    public const int MaxMemoryMb = 150;
    public const int IdleMemoryMb = 25;
    public const int ModelMaxMemoryMb = 50;
    public const int TargetInferenceMs = 200;
    public const int TargetRuleBasedMs = 5;
    public const int PopupAppearLatencyMs = 100;
    public const int LazyLoadDelayMs = 5000;

    #endregion

    #region Hook & UI Defaults

    public const int DefaultTypingPauseMs = 1500;
    public const int PopupDismissTimeoutMs = 5000;
    public const int PopupMaxWidth = 400;
    public const int PopupMinWidth = 200;
    public const int PopupOffsetY = 8;
    public const int MaxSuggestions = 5;
    public const int CircuitBreakerThreshold = 3;
    public const int CircuitBreakerRecoveryMs = 60_000;

    #endregion

    #region Paths

    /// <summary>Gets the application data directory path.</summary>
    public static string AppDataPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        AppDataFolderName);

    /// <summary>Gets the crash log directory path.</summary>
    public static string CrashLogPath => Path.Combine(AppDataPath, "crash_logs");

    /// <summary>Gets the settings file path.</summary>
    public static string SettingsFilePath => Path.Combine(AppDataPath, "settings.json");

    /// <summary>Gets the adaptive learning database path.</summary>
    public static string LearningDbPath => Path.Combine(AppDataPath, "learning.dat");

    /// <summary>Gets the plugins directory path.</summary>
    public static string PluginsPath => Path.Combine(AppDataPath, "plugins");

    #endregion
}
