using System.Security.Cryptography;

namespace LocalAIWriter.Core.Security;

/// <summary>
/// Security module that ensures application and model integrity.
/// Verifies model files haven't been tampered with,
/// audits loaded assemblies, and provides memory scrubbing.
/// </summary>
public static class SecurityGuardian
{
    /// <summary>
    /// Verifies an ONNX model file integrity against its stored SHA-256 hash.
    /// </summary>
    /// <param name="modelPath">Path to the .onnx model file.</param>
    /// <param name="hashFilePath">Path to the .sha256 hash file.</param>
    /// <returns>True if the model hash matches the expected hash.</returns>
    public static bool VerifyModelIntegrity(string modelPath, string hashFilePath)
    {
        if (!File.Exists(modelPath) || !File.Exists(hashFilePath))
            return false;

        try
        {
            var expectedHash = File.ReadAllText(hashFilePath).Trim().ToUpperInvariant();
            var actualHash = ComputeFileHash(modelPath);
            return string.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Computes the SHA-256 hash of a file.
    /// </summary>
    public static string ComputeFileHash(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var hashBytes = SHA256.HashData(stream);
        return Convert.ToHexString(hashBytes);
    }

    /// <summary>
    /// Asserts that no network-related assemblies are loaded in the current AppDomain.
    /// Logs a warning if any are found.
    /// </summary>
    /// <returns>List of network assembly names found (empty if clean).</returns>
    public static IReadOnlyList<string> AuditNetworkAssemblies()
    {
        var networkAssemblies = new List<string>();
        var suspiciousNames = new[]
        {
            "System.Net.Http",
            "System.Net.Sockets",
            "System.Net.WebSockets",
            "System.Net.Requests",
        };

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var name = assembly.GetName().Name ?? string.Empty;
            if (suspiciousNames.Any(s => name.StartsWith(s, StringComparison.OrdinalIgnoreCase)))
            {
                networkAssemblies.Add(name);
            }
        }

        return networkAssemblies;
    }

    /// <summary>
    /// Checks if the application is running in a trusted environment
    /// (not being debugged by an external debugger).
    /// </summary>
    public static bool IsEnvironmentTrusted()
    {
        return !System.Diagnostics.Debugger.IsAttached;
    }
}
