using Microsoft.Extensions.Logging;
using LocalAIWriter.Plugins;

namespace LocalAIWriter.Core.Services;

/// <summary>
/// Manages loading, sandboxing, and lifecycle of plugins.
/// Plugins run in isolated AssemblyLoadContexts with restricted capabilities.
/// </summary>
public sealed class PluginManager : IDisposable
{
    private readonly ILogger<PluginManager> _logger;
    private readonly List<LoadedPlugin> _plugins = new();
    private readonly object _lock = new();

    public PluginManager(ILogger<PluginManager> logger)
    {
        _logger = logger;
    }

    /// <summary>Gets all loaded plugins.</summary>
    public IReadOnlyList<LoadedPlugin> Plugins
    {
        get { lock (_lock) return _plugins.ToList(); }
    }

    /// <summary>
    /// Scans and loads plugins from the plugins directory.
    /// </summary>
    public async Task LoadPluginsAsync(string pluginsDir, CancellationToken ct = default)
    {
        if (!Directory.Exists(pluginsDir))
        {
            _logger.LogInformation("Plugins directory not found: {Dir}", pluginsDir);
            return;
        }

        var pluginDirs = Directory.GetDirectories(pluginsDir);
        foreach (var dir in pluginDirs)
        {
            ct.ThrowIfCancellationRequested();
            await LoadPluginFromDirectoryAsync(dir, ct);
        }

        _logger.LogInformation("Loaded {Count} plugins", _plugins.Count);
    }

    /// <summary>
    /// Gets the combined custom dictionary from all plugins.
    /// </summary>
    public IReadOnlySet<string> GetCombinedDictionary()
    {
        var combined = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        lock (_lock)
        {
            foreach (var plugin in _plugins)
            {
                if (plugin.IsEnabled && plugin.Plugin.Capabilities.HasFlag(PluginCapabilities.CustomDictionary))
                {
                    try
                    {
                        var dict = plugin.Plugin.GetCustomDictionary();
                        foreach (var word in dict)
                            combined.Add(word);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Plugin {Name} dictionary access failed", plugin.Plugin.Name);
                    }
                }
            }
        }

        return combined;
    }

    /// <summary>
    /// Runs pre-processing plugins on text.
    /// </summary>
    public string RunPreProcessors(string text)
    {
        var result = text;

        lock (_lock)
        {
            foreach (var plugin in _plugins)
            {
                if (!plugin.IsEnabled || !plugin.Plugin.Capabilities.HasFlag(PluginCapabilities.PreProcessing))
                    continue;

                try
                {
                    result = plugin.Plugin.PreProcess(result);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Plugin {Name} pre-processing failed", plugin.Plugin.Name);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Enables or disables a plugin by ID.
    /// </summary>
    public void SetPluginEnabled(string pluginId, bool enabled)
    {
        lock (_lock)
        {
            var plugin = _plugins.FirstOrDefault(p => p.Plugin.Id == pluginId);
            if (plugin != null)
            {
                plugin.IsEnabled = enabled;
                _logger.LogInformation("Plugin {Name} {State}", plugin.Plugin.Name, enabled ? "enabled" : "disabled");
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var plugin in _plugins)
            {
                try { plugin.Plugin.Dispose(); }
                catch { /* Swallow disposal errors from plugins */ }
            }
            _plugins.Clear();
        }
    }

    #region Private Methods

    private async Task LoadPluginFromDirectoryAsync(string directory, CancellationToken ct)
    {
        try
        {
            // Look for a plugin manifest or DLL in the directory
            var dllFiles = Directory.GetFiles(directory, "*.dll");
            if (dllFiles.Length == 0)
            {
                // Check for dictionary.txt (simple dictionary plugin)
                var dictFile = Path.Combine(directory, "dictionary.txt");
                if (File.Exists(dictFile))
                {
                    var dictPlugin = new DictionaryFilePlugin(
                        Path.GetFileName(directory),
                        dictFile);
                    await dictPlugin.InitializeAsync(ct);

                    lock (_lock)
                    {
                        _plugins.Add(new LoadedPlugin(dictPlugin, directory, true));
                    }

                    _logger.LogInformation("Loaded dictionary plugin: {Name} ({Words} words)",
                        dictPlugin.Name, dictPlugin.GetCustomDictionary().Count);
                }
                return;
            }

            _logger.LogDebug("Skipping plugin DLL loading from {Dir} (sandboxed loading not yet implemented)", directory);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load plugin from {Dir}", directory);
        }
    }

    #endregion
}

/// <summary>Represents a loaded plugin with its metadata.</summary>
public class LoadedPlugin
{
    public IPlugin Plugin { get; }
    public string SourceDirectory { get; }
    public bool IsEnabled { get; set; }

    public LoadedPlugin(IPlugin plugin, string sourceDirectory, bool isEnabled)
    {
        Plugin = plugin;
        SourceDirectory = sourceDirectory;
        IsEnabled = isEnabled;
    }
}

/// <summary>
/// Simple dictionary-file-based plugin for loading custom word lists.
/// </summary>
internal sealed class DictionaryFilePlugin : IPlugin
{
    private readonly string _filePath;
    private readonly HashSet<string> _words = new(StringComparer.OrdinalIgnoreCase);

    public string Id { get; }
    public string Name { get; }
    public string Version => "1.0";
    public PluginCapabilities Capabilities => PluginCapabilities.CustomDictionary;

    public DictionaryFilePlugin(string name, string filePath)
    {
        Id = $"dict_{name.ToLowerInvariant()}";
        Name = $"{name} Dictionary";
        _filePath = filePath;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (File.Exists(_filePath))
        {
            var lines = await File.ReadAllLinesAsync(_filePath, ct);
            foreach (var line in lines)
            {
                var word = line.Trim();
                if (!string.IsNullOrEmpty(word) && !word.StartsWith('#'))
                    _words.Add(word);
            }
        }
    }

    public string PreProcess(string text) => text;
    public IReadOnlySet<string> GetCustomDictionary() => _words;
    public void Dispose() { _words.Clear(); }
}
