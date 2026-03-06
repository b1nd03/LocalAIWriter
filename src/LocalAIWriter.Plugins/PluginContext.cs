namespace LocalAIWriter.Plugins;

/// <summary>
/// Provides sandboxed context to plugins, limiting their access
/// to only their own data directory and basic text operations.
/// </summary>
public sealed class PluginContext
{
    /// <summary>Gets the plugin's private data directory. Plugins may read/write here only.</summary>
    public string DataDirectory { get; }

    /// <summary>Gets the plugin ID.</summary>
    public string PluginId { get; }

    public PluginContext(string pluginId, string dataDirectory)
    {
        PluginId = pluginId;
        DataDirectory = dataDirectory;

        // Ensure the data directory exists
        if (!Directory.Exists(dataDirectory))
            Directory.CreateDirectory(dataDirectory);
    }

    /// <summary>
    /// Reads a file from the plugin's data directory.
    /// Throws if the path is outside the data directory (security sandbox).
    /// </summary>
    public string ReadFile(string relativePath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(DataDirectory, relativePath));
        ValidatePath(fullPath);
        return File.ReadAllText(fullPath);
    }

    /// <summary>
    /// Writes a file to the plugin's data directory.
    /// Throws if the path is outside the data directory (security sandbox).
    /// </summary>
    public void WriteFile(string relativePath, string content)
    {
        var fullPath = Path.GetFullPath(Path.Combine(DataDirectory, relativePath));
        ValidatePath(fullPath);
        File.WriteAllText(fullPath, content);
    }

    /// <summary>
    /// Checks if a file exists in the plugin's data directory.
    /// </summary>
    public bool FileExists(string relativePath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(DataDirectory, relativePath));
        ValidatePath(fullPath);
        return File.Exists(fullPath);
    }

    private void ValidatePath(string fullPath)
    {
        var normalizedData = Path.GetFullPath(DataDirectory);
        if (!fullPath.StartsWith(normalizedData, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException(
                $"Plugin {PluginId} attempted to access path outside its data directory.");
        }
    }
}
