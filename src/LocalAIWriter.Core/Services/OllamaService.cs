using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace LocalAIWriter.Core.Services;

/// <summary>
/// Service to communicate with Ollama for AI grammar correction.
/// Supports configurable endpoint and model via settings.
/// </summary>
public sealed class OllamaService : IDisposable
{
    private readonly HttpClient _http;
    private readonly ILogger<OllamaService>? _logger;
    private const string DefaultBaseUrl = "http://localhost:11434";
    private const string DefaultModel = "gemma2:2b";
    private const int TimeoutSeconds = 120;

    private static readonly string SystemPrompt =
        "You are a grammar correction tool. You receive text and return ONLY the corrected version.\n\n" +
        "RULES:\n" +
        "- Fix grammar, spelling, and punctuation ONLY\n" +
        "- Output ONLY the corrected text, nothing else\n" +
        "- NO explanations, NO reasoning, NO quotes around the output\n" +
        "- Do NOT start with phrases like 'Here is', 'The corrected', 'Sure', 'We are given'\n" +
        "- Keep the same meaning, tone, length, and style\n" +
        "- If already correct, return it unchanged\n\n" +
        "EXAMPLES:\n" +
        "Input: he are too sweet\n" +
        "Output: He is too sweet.\n\n" +
        "Input: i cant beleive how beutiful the sunset is\n" +
        "Output: I can't believe how beautiful the sunset is.\n\n" +
        "Input: she dont like going to school becuz its boring\n" +
        "Output: She doesn't like going to school because it's boring.";

    public OllamaService(ILogger<OllamaService>? logger = null)
    {
        _logger = logger;
        _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(TimeoutSeconds)
        };
    }

    private static void LogDebug(string msg)
    {
        try
        {
            var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_log.txt");
            File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] OllamaService: {msg}{Environment.NewLine}");
        }
        catch { }
    }


    /// <summary>
    /// Gets the active Ollama endpoint and model from settings.
    /// </summary>
    public (string Endpoint, string Model) GetActiveConfiguration()
    {
        var (endpoint, model) = LoadSettingsConfig();
        return (endpoint, model);
    }

    /// <summary>
    /// Load endpoint and model from settings.json, falling back to defaults.
    /// </summary>
    private static (string Endpoint, string Model) LoadSettingsConfig()
    {
        var endpoint = DefaultBaseUrl;
        var model = DefaultModel;

        try
        {
            var path = Constants.SettingsFilePath;
            if (File.Exists(path))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                var root = doc.RootElement;

                if (root.TryGetProperty("OllamaEndpoint", out var ep))
                {
                    var val = ep.GetString()?.Trim().TrimEnd('/');
                    if (!string.IsNullOrWhiteSpace(val)) endpoint = val;
                }
                if (root.TryGetProperty("OllamaModel", out var m))
                {
                    var val = m.GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(val)) model = val;
                }
            }
        }
        catch { /* use defaults */ }

        return (endpoint, model);
    }


    /// <summary>
    /// Lists all models available on the configured (or overridden) Ollama endpoint.
    /// </summary>
    public async Task<OllamaModelCatalog> ListAvailableModelsAsync(
        string? endpointOverride = null, string? modelOverride = null)
    {
        var (cfgEndpoint, cfgModel) = LoadSettingsConfig();
        var endpoint = string.IsNullOrWhiteSpace(endpointOverride) ? cfgEndpoint : endpointOverride.Trim().TrimEnd('/');
        var model = string.IsNullOrWhiteSpace(modelOverride) ? cfgModel : modelOverride.Trim();

        try
        {
            var url = $"{endpoint}/api/tags";
            var response = await _http.GetAsync(url);
            if (!response.IsSuccessStatusCode)
                return new OllamaModelCatalog(false, $"Endpoint responded with {(int)response.StatusCode}",
                    endpoint, model, Array.Empty<string>());

            var json = await response.Content.ReadAsStringAsync();
            var models = ParseModelNames(json);

            return new OllamaModelCatalog(true,
                models.Count > 0 ? $"Connected ({models.Count} models)" : "Connected (no models)",
                endpoint, model, models);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new OllamaModelCatalog(false, "Cannot connect to Ollama endpoint",
                endpoint, model, Array.Empty<string>());
        }
    }

    private static IReadOnlyList<string> ParseModelNames(string json)
    {
        var models = new List<string>();
        try
        {
            var doc = JsonNode.Parse(json);
            var arr = doc?["models"]?.AsArray();
            if (arr != null)
            {
                foreach (var item in arr)
                {
                    var name = item?["name"]?.GetValue<string>();
                    if (!string.IsNullOrWhiteSpace(name))
                        models.Add(name);
                }
            }
        }
        catch { }
        return models.Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
    }


    /// <summary>
    /// Check if Ollama is running and the configured model is available.
    /// </summary>
    public async Task<(bool Running, bool ModelReady, string Status)> CheckStatusAsync()
    {
        var (endpoint, model) = LoadSettingsConfig();
        try
        {
            var response = await _http.GetAsync($"{endpoint}/api/tags");
            if (!response.IsSuccessStatusCode)
                return (false, false, "Ollama not responding");

            var json = await response.Content.ReadAsStringAsync();
            bool hasModel = json.Contains(model, StringComparison.OrdinalIgnoreCase);
            return (true, hasModel,
                hasModel ? "Ready" : $"Model not installed — run: ollama pull {model}");
        }
        catch (HttpRequestException)
        {
            return (false, false, $"Ollama not running ({endpoint})");
        }
        catch (TaskCanceledException)
        {
            return (false, false, "Ollama connection timed out");
        }
    }


    /// <summary>
    /// Correct grammar using the configured Ollama model.
    /// </summary>
    public async Task<OllamaResult> CorrectGrammarAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new OllamaResult(text, false, "Empty input");

        try
        {
            var sentences = SplitIntoSentences(text);
            if (sentences.Count == 0)
                return new OllamaResult(text, false, "No sentences found");

            LogDebug($"Input: '{text}', Sentences: {sentences.Count}");

            var correctedSentences = new List<string>();
            int totalCorrections = 0;

            foreach (var sentence in sentences)
            {
                if (string.IsNullOrWhiteSpace(sentence))
                {
                    correctedSentences.Add(sentence);
                    continue;
                }

                var corrected = await CorrectSingleSentenceAsync(sentence.Trim());
                LogDebug($"  Sentence: '{sentence.Trim()}' → '{corrected}'");

                if (corrected != null && !string.Equals(corrected.Trim(), sentence.Trim(), StringComparison.Ordinal))
                    totalCorrections++;

                correctedSentences.Add(corrected ?? sentence.Trim());
            }

            // Join with single space for single-line input, keep \n\n for multi-line
            var result = sentences.Count == 1
                ? correctedSentences[0]
                : string.Join("\n\n", correctedSentences);

            // Normalize comparison - ignore leading/trailing whitespace
            bool changed = !string.Equals(result.Trim(), text.Trim(), StringComparison.Ordinal) && totalCorrections > 0;
            LogDebug($"Result: '{result}', Changed={changed}, Corrections={totalCorrections}");

            return new OllamaResult(result, true,
                changed ? $"{totalCorrections} sentence{(totalCorrections > 1 ? "s" : "")} corrected"
                        : "No changes needed");
        }
        catch (HttpRequestException ex)
        {
            _logger?.LogError(ex, "Ollama connection failed");
            return new OllamaResult(text, false, "Ollama not running — start it from the system tray");
        }
        catch (TaskCanceledException)
        {
            return new OllamaResult(text, false, "Request timed out — try shorter text");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Ollama error");
            return new OllamaResult(text, false, $"Error: {ex.Message}");
        }
    }

    private async Task<string?> CorrectSingleSentenceAsync(string sentence)
    {
        var (endpoint, model) = LoadSettingsConfig();

        // Use /api/chat with message array — works much better with thinking models
        var payload = new
        {
            model = model,
            stream = false,
            think = false,
            messages = new[]
            {
                new { role = "system", content = SystemPrompt },
                new { role = "user", content = sentence }
            },
            options = new
            {
                temperature = 0.1,
                top_p = 0.9,
                num_predict = 256
            }
        };

        var jsonPayload = JsonSerializer.Serialize(payload);
        var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        var response = await _http.PostAsync($"{endpoint}/api/chat", content);
        if (!response.IsSuccessStatusCode)
        {
            LogDebug($"  Ollama HTTP error: {response.StatusCode}");
            return sentence;
        }

        var responseJson = await response.Content.ReadAsStringAsync();
        LogDebug($"  Ollama raw response: {responseJson.Substring(0, Math.Min(400, responseJson.Length))}");

        using var doc = JsonDocument.Parse(responseJson);

        // /api/chat returns { message: { role: "assistant", content: "..." } }
        string? corrected = null;
        if (doc.RootElement.TryGetProperty("message", out var messageProp)
            && messageProp.TryGetProperty("content", out var contentProp))
        {
            corrected = contentProp.GetString()?.Trim();
        }

        if (string.IsNullOrWhiteSpace(corrected))
        {
            LogDebug("  No content in Ollama chat response");
            return sentence;
        }

        LogDebug($"  Ollama corrected: '{corrected}'");
        var validated = ValidateOutput(sentence, corrected);
        LogDebug($"  After validation: '{validated}'");
        return validated;
    }

    // ──── Output Validation ────

    private static string ValidateOutput(string original, string output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return original;

        output = output.Replace("```", "").Replace("**", "").Trim();

        // Check if the output looks like AI reasoning (even if single-line)
        bool looksLikeReasoning = output.Contains("We are given", StringComparison.OrdinalIgnoreCase)
            || output.Contains("Step 1", StringComparison.OrdinalIgnoreCase)
            || output.Contains("Let me", StringComparison.OrdinalIgnoreCase)
            || output.Contains("sentence:", StringComparison.OrdinalIgnoreCase)
            || output.Contains("The corrected", StringComparison.OrdinalIgnoreCase)
            || output.Contains("Original:", StringComparison.OrdinalIgnoreCase)
            || output.Contains("Correction:", StringComparison.OrdinalIgnoreCase)
            || output.Contains('\n');

        // If output is short, clean, and NOT reasoning — return directly
        if (!looksLikeReasoning && output.Length <= original.Length * 2.5)
        {
            return CleanFinalOutput(original, output);
        }

        // Reasoning model output — extract the actual corrected text
        LogDebug($"  ValidateOutput: Reasoning detected ({output.Length} chars), extracting corrected text...");

        // Pattern 1: Look for quoted text after keywords like "Correction:", "corrected text is", etc.
        var extractionPatterns = new[]
        {
            @"[Cc]orrect(?:ion|ed\s+text)\s*(?:is|:)\s*""([^""]+)""",
            @"[Cc]orrect(?:ion|ed)\s*:\s*""([^""]+)""",
            @"[Oo]utput\s*:\s*""([^""]+)""",
            @"[Rr]esult\s*:\s*""([^""]+)""",
            @"[Ff]inal\s*(?:text|answer|version|output)\s*(?:is|:)\s*""([^""]+)""",
            @"[Ss]hould\s+be\s*:?\s*""([^""]+)""",
            @"[Ss]entence\s*:\s*""([^""]+)""",
        };

        foreach (var pattern in extractionPatterns)
        {
            // Use RegexOptions.RightToLeft for "sentence:" to get the LAST match (corrected, not original)
            var opts = pattern.Contains("entence")
                ? System.Text.RegularExpressions.RegexOptions.RightToLeft
                : System.Text.RegularExpressions.RegexOptions.None;
            var match = System.Text.RegularExpressions.Regex.Match(output, pattern, opts);
            if (match.Success && match.Groups[1].Value.Length > 0)
            {
                var extracted = match.Groups[1].Value.Trim();
                LogDebug($"  ValidateOutput: Extracted via pattern: '{extracted}'");
                if (extracted.Length >= original.Length * 0.3 && extracted.Length <= original.Length * 3
                    && !extracted.Equals(original, StringComparison.OrdinalIgnoreCase))
                    return extracted;
            }
        }

        // Pattern 2: Look for the LAST quoted text that's similar in length to the original
        var quotedMatches = System.Text.RegularExpressions.Regex.Matches(output, @"""([^""]{3,})""");
        string? bestQuoted = null;
        foreach (System.Text.RegularExpressions.Match m in quotedMatches)
        {
            var q = m.Groups[1].Value.Trim();
            if (q.Length >= original.Length * 0.5 && q.Length <= original.Length * 2 && q != original)
                bestQuoted = q;
        }
        if (bestQuoted != null)
        {
            LogDebug($"  ValidateOutput: Best quoted match: '{bestQuoted}'");
            return bestQuoted;
        }

        // Pattern 3: Try first line only (old behavior)
        var firstLine = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)[0].Trim();
        if (firstLine.Length <= original.Length * 2.5 && firstLine.Length >= original.Length * 0.3
            && !looksLikeReasoning)
        {
            return CleanFinalOutput(original, firstLine);
        }

        LogDebug($"  ValidateOutput: Could not extract, returning original");
        return original;
    }

    private static string CleanFinalOutput(string original, string output)
    {
        // Remove common AI prefixes
        var prefixes = new[] {
            "Here is the corrected", "Here's the corrected",
            "Corrected text:", "Corrected:", "The corrected",
            "Sure,", "Sure!", "Of course", "I'd be happy",
            "Here you go", "The sentence should be"
        };
        foreach (var prefix in prefixes)
        {
            if (output.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var afterPrefix = output[prefix.Length..].TrimStart(':', ' ', '\n', '\r');
                if (!string.IsNullOrWhiteSpace(afterPrefix))
                    output = afterPrefix;
            }
        }

        // Remove surrounding quotes
        if (output.StartsWith('"') && output.EndsWith('"') && output.Length > 2)
            output = output[1..^1];

        if (output.Length > original.Length * 2.5) return original;
        if (output.Length < original.Length * 0.2) return original;

        return output.Trim();
    }

    private static List<string> SplitIntoSentences(string text)
    {
        var result = new List<string>();
        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (!string.IsNullOrEmpty(trimmed))
                result.Add(trimmed);
        }
        return result;
    }

    public void Dispose() => _http.Dispose();
}

/// <summary>Result from Ollama grammar correction.</summary>
public record OllamaResult(string CorrectedText, bool Success, string Message);

/// <summary>Catalog of models from a configured Ollama endpoint.</summary>
public record OllamaModelCatalog(
    bool Success, string Status, string Endpoint,
    string ActiveModel, IReadOnlyList<string> Models);
