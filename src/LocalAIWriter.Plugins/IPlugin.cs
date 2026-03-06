namespace LocalAIWriter.Plugins;

/// <summary>
/// Interface for LocalAI Writer plugins. All plugins run sandboxed
/// with no network access and limited file system access.
/// </summary>
public interface IPlugin : IDisposable
{
    /// <summary>Unique plugin identifier.</summary>
    string Id { get; }

    /// <summary>Human-readable plugin name.</summary>
    string Name { get; }

    /// <summary>Plugin version string.</summary>
    string Version { get; }

    /// <summary>Flags indicating what capabilities this plugin provides.</summary>
    PluginCapabilities Capabilities { get; }

    /// <summary>
    /// Called when plugin is loaded. Initialize resources here.
    /// </summary>
    Task InitializeAsync(CancellationToken ct = default);

    /// <summary>
    /// Pre-process text before ML inference.
    /// Only called if <see cref="PluginCapabilities.PreProcessing"/> is set.
    /// </summary>
    string PreProcess(string text);

    /// <summary>
    /// Provide additional dictionary entries (domain-specific words
    /// that should not be flagged as misspellings).
    /// Only called if <see cref="PluginCapabilities.CustomDictionary"/> is set.
    /// </summary>
    IReadOnlySet<string> GetCustomDictionary();
}

/// <summary>
/// Flags indicating what capabilities a plugin provides.
/// </summary>
[Flags]
public enum PluginCapabilities
{
    /// <summary>No special capabilities.</summary>
    None = 0,

    /// <summary>Adds domain-specific words to the dictionary.</summary>
    CustomDictionary = 1,

    /// <summary>Modifies text before inference.</summary>
    PreProcessing = 2,

    /// <summary>Filters or modifies corrections after inference.</summary>
    PostProcessing = 4,

    /// <summary>Adds custom rule-based corrections.</summary>
    CustomRules = 8,

    /// <summary>Provides style guide enforcement.</summary>
    StyleGuide = 16
}
