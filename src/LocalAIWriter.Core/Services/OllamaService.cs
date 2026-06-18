using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace LocalAIWriter.Core.Services;

/// <summary>
/// Communicates with Ollama for local grammar correction.
/// </summary>
public sealed class OllamaService : IDisposable
{
    private readonly HttpClient _http;
    private readonly ILogger<OllamaService>? _logger;
    private const string DefaultBaseUrl = "http://localhost:11434";
    private const string DefaultModel = "gemma2:2b";
    private const int TimeoutSeconds = 120;

    private static readonly string SystemPrompt =
        "You are a grammar correction tool. Return ONLY compact JSON in this exact shape: " +
        "{\"corrected_text\":\"...\",\"changed\":true}. " +
        "Fix grammar, spelling, punctuation, and phrasing only. Keep the same meaning, tone, and style. " +
        "Do not explain, score, analyze, or include markdown. If the text is already correct, return it unchanged with changed=false.";

    public OllamaService(ILogger<OllamaService>? logger = null)
    {
        _logger = logger;
        _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(TimeoutSeconds)
        };
    }

    public OllamaService(HttpMessageHandler handler, ILogger<OllamaService>? logger = null)
    {
        _logger = logger;
        _http = new HttpClient(handler)
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
        catch
        {
        }
    }

    public (string Endpoint, string Model) GetActiveConfiguration()
    {
        var (endpoint, model) = LoadSettingsConfig();
        return (endpoint, model);
    }

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
                    if (!string.IsNullOrWhiteSpace(val))
                        endpoint = val;
                }

                if (root.TryGetProperty("OllamaModel", out var m))
                {
                    var val = m.GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(val))
                        model = val;
                }
            }
        }
        catch
        {
        }

        return (endpoint, model);
    }

    public async Task<OllamaModelCatalog> ListAvailableModelsAsync(
        string? endpointOverride = null,
        string? modelOverride = null)
    {
        var (cfgEndpoint, cfgModel) = LoadSettingsConfig();
        var endpoint = string.IsNullOrWhiteSpace(endpointOverride) ? cfgEndpoint : endpointOverride.Trim().TrimEnd('/');
        var model = string.IsNullOrWhiteSpace(modelOverride) ? cfgModel : modelOverride.Trim();

        try
        {
            var response = await _http.GetAsync($"{endpoint}/api/tags");
            if (!response.IsSuccessStatusCode)
            {
                return new OllamaModelCatalog(
                    false,
                    $"Endpoint responded with {(int)response.StatusCode}",
                    endpoint,
                    model,
                    Array.Empty<string>());
            }

            var json = await response.Content.ReadAsStringAsync();
            var models = ParseModelNames(json);

            return new OllamaModelCatalog(
                true,
                models.Count > 0 ? $"Connected ({models.Count} models)" : "Connected (no models)",
                endpoint,
                model,
                models);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            return new OllamaModelCatalog(false, "Cannot connect to Ollama endpoint", endpoint, model, Array.Empty<string>());
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
        catch
        {
        }

        return models
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public Task<(bool Running, bool ModelReady, string Status)> CheckStatusAsync() =>
        CheckStatusAsync(CancellationToken.None);

    public async Task<(bool Running, bool ModelReady, string Status)> CheckStatusAsync(CancellationToken ct)
    {
        var (endpoint, model) = LoadSettingsConfig();
        try
        {
            var response = await _http.GetAsync($"{endpoint}/api/tags", ct);
            if (!response.IsSuccessStatusCode)
                return (false, false, "Ollama not responding");

            var json = await response.Content.ReadAsStringAsync(ct);
            var hasModel = json.Contains(model, StringComparison.OrdinalIgnoreCase);
            return (true, hasModel, hasModel ? "Ready" : $"Model not installed - run: ollama pull {model}");
        }
        catch (HttpRequestException)
        {
            return (false, false, $"Ollama not running ({endpoint})");
        }
        catch (TaskCanceledException)
        {
            return (false, false, "Ollama connection timed out");
        }
        catch (OperationCanceledException)
        {
            return (false, false, "Ollama status check canceled");
        }
    }

    public Task<OllamaResult> CorrectGrammarAsync(string text) =>
        CorrectGrammarAsync(text, CorrectionSafetyMode.Conservative, CancellationToken.None);

    public Task<OllamaResult> CorrectGrammarAsync(string text, CancellationToken ct) =>
        CorrectGrammarAsync(text, CorrectionSafetyMode.Conservative, ct);

    public async Task<OllamaResult> CorrectGrammarAsync(
        string text,
        CorrectionSafetyMode safetyMode,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new OllamaResult(text, false, "Empty input", false, false, "empty_input");

        if (ct.IsCancellationRequested)
            return new OllamaResult(text, false, "Request canceled", false, false, "canceled");

        if (IsMetaEvaluationInput(text))
            return new OllamaResult(text, false, "Meta evaluation input ignored", false, false, "meta_evaluation_input");

        try
        {
            var sentences = SplitIntoSentences(text);
            if (sentences.Count == 0)
                return new OllamaResult(text, false, "No sentences found", false, false, "empty_input");

            var correctedSentences = new List<string>();
            var totalCorrections = 0;
            var usedRetry = false;

            foreach (var sentence in sentences)
            {
                if (string.IsNullOrWhiteSpace(sentence))
                {
                    correctedSentences.Add(sentence);
                    continue;
                }

                var sentenceResult = await CorrectSingleSentenceAsync(sentence.Trim(), safetyMode, ct);
                usedRetry |= sentenceResult.UsedRetry;

                if (!sentenceResult.Success
                    && sentenceResult.RejectedReason is "invalid_json" or "meta_evaluation_output" or "explanatory_output")
                {
                    return sentenceResult with { CorrectedText = text };
                }

                if (!string.Equals(sentenceResult.CorrectedText.Trim(), sentence.Trim(), StringComparison.Ordinal))
                    totalCorrections++;

                correctedSentences.Add(sentenceResult.CorrectedText);
            }

            var result = sentences.Count == 1
                ? correctedSentences[0]
                : string.Join("\n\n", correctedSentences);
            var changed = !string.Equals(result.Trim(), text.Trim(), StringComparison.Ordinal) && totalCorrections > 0;

            return new OllamaResult(
                result,
                true,
                changed ? $"{totalCorrections} sentence{(totalCorrections > 1 ? "s" : "")} corrected" : "No changes needed",
                true,
                usedRetry);
        }
        catch (HttpRequestException ex)
        {
            _logger?.LogError(ex, "Ollama connection failed");
            return new OllamaResult(text, false, "Ollama not running - start it from the system tray", false, false, "connection_failed");
        }
        catch (TaskCanceledException)
        {
            return new OllamaResult(text, false, "Request timed out - try shorter text", false, false, "timeout");
        }
        catch (OperationCanceledException)
        {
            return new OllamaResult(text, false, "Request canceled", false, false, "canceled");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Ollama error");
            return new OllamaResult(text, false, $"Error: {ex.Message}", false, false, "exception");
        }
    }

    private async Task<OllamaResult> CorrectSingleSentenceAsync(
        string sentence,
        CorrectionSafetyMode safetyMode,
        CancellationToken ct)
    {
        var (endpoint, model) = LoadSettingsConfig();
        var content = BuildChatRequest(sentence, model);

        var response = await _http.PostAsync($"{endpoint}/api/chat", content, ct);
        if (!response.IsSuccessStatusCode)
        {
            LogDebug($"Ollama HTTP error: {response.StatusCode}");
            return new OllamaResult(sentence, false, $"Ollama HTTP error: {response.StatusCode}", false, false, "http_error");
        }

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        LogDebug($"Ollama raw response: {responseJson[..Math.Min(400, responseJson.Length)]}");

        var first = BuildResultFromChatResponse(sentence, responseJson, safetyMode, usedRetry: false);
        if (first.Success || first.RejectedReason != "invalid_json")
            return first;

        var retryContent = BuildChatRequest(sentence, model);
        var retryResponse = await _http.PostAsync($"{endpoint}/api/chat", retryContent, ct);
        if (!retryResponse.IsSuccessStatusCode)
            return first with { UsedRetry = true };

        var retryJson = await retryResponse.Content.ReadAsStringAsync(ct);
        LogDebug($"Ollama retry raw response: {retryJson[..Math.Min(400, retryJson.Length)]}");
        return BuildResultFromChatResponse(sentence, retryJson, safetyMode, usedRetry: true);
    }

    private static StringContent BuildChatRequest(string sentence, string model)
    {
        var payload = new
        {
            model,
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
        return new StringContent(jsonPayload, Encoding.UTF8, "application/json");
    }

    private static OllamaResult BuildResultFromChatResponse(
        string original,
        string responseJson,
        CorrectionSafetyMode safetyMode,
        bool usedRetry)
    {
        if (!TryExtractStructuredCorrectionFromChatResponse(
                responseJson,
                out var structuredCorrection,
                out var changed,
                out var structuredReason))
        {
            var rawContent = ExtractChatContent(responseJson);
            if (!string.IsNullOrWhiteSpace(rawContent) && LooksLikePlainCorrection(rawContent, original))
            {
                var plainValidation = ValidateCandidateOutput(original, rawContent, safetyMode);
                return new OllamaResult(
                    plainValidation.SanitizedText,
                    plainValidation.IsValid,
                    plainValidation.IsValid ? "Corrected" : "Output rejected",
                    plainValidation.IsValid,
                    usedRetry,
                    plainValidation.Reason);
            }

            return new OllamaResult(
                original,
                false,
                "Invalid Ollama response",
                false,
                usedRetry,
                structuredReason ?? "invalid_json");
        }

        var candidate = changed ? structuredCorrection : original;
        var validation = ValidateCandidateOutput(original, candidate, safetyMode);
        return new OllamaResult(
            validation.SanitizedText,
            validation.IsValid,
            validation.IsValid ? changed ? "Corrected" : "No changes needed" : "Output rejected",
            validation.IsValid,
            usedRetry,
            validation.Reason);
    }

    public static bool TryExtractStructuredCorrectionFromChatResponse(
        string responseJson,
        out string correctedText,
        out bool changed,
        out string? reason)
    {
        correctedText = "";
        changed = false;
        reason = null;

        var content = ExtractChatContent(responseJson);
        if (string.IsNullOrWhiteSpace(content))
        {
            reason = "missing_content";
            return false;
        }

        try
        {
            using var contentDoc = JsonDocument.Parse(content);
            var root = contentDoc.RootElement;
            if (!root.TryGetProperty("corrected_text", out var correctedProperty))
            {
                reason = "missing_corrected_text";
                return false;
            }

            correctedText = correctedProperty.GetString()?.Trim() ?? "";
            if (root.TryGetProperty("changed", out var changedProperty)
                && changedProperty.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                changed = changedProperty.GetBoolean();
            }
            else
            {
                changed = true;
            }

            return !string.IsNullOrWhiteSpace(correctedText);
        }
        catch (JsonException)
        {
            reason = "invalid_json";
            return false;
        }
    }

    private static string? ExtractChatContent(string responseJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseJson);
            if (doc.RootElement.TryGetProperty("message", out var messageProp)
                && messageProp.TryGetProperty("content", out var contentProp))
            {
                return contentProp.GetString()?.Trim();
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    public static bool IsMetaEvaluationInput(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var markers = new[]
        {
            "total sentences",
            "correct:",
            "incorrect:",
            "accuracy:",
            "score:",
            "what this test shows",
            "grammatically acceptable"
        };

        return markers.Count(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase)) >= 2;
    }

    public static CandidateValidation ValidateCandidateOutput(
        string original,
        string candidate,
        CorrectionSafetyMode safetyMode)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            return new CandidateValidation(false, original, "empty_output");

        var cleaned = SanitizeCandidateOutput(candidate);
        if (IsMetaEvaluationInput(cleaned))
            return new CandidateValidation(false, original, "meta_evaluation_output");

        if (LooksExplanatory(cleaned))
            return new CandidateValidation(false, original, "explanatory_output");

        var originalLength = Math.Max(1, original.Trim().Length);
        var ratio = cleaned.Length / (double)originalLength;
        if (ratio < 0.2 || ratio > 2.5)
            return new CandidateValidation(false, original, "length_ratio_out_of_bounds");

        if (safetyMode == CorrectionSafetyMode.Conservative
            && CountSentences(original) != CountSentences(cleaned))
        {
            return new CandidateValidation(false, original, "sentence_count_shift");
        }

        return new CandidateValidation(true, cleaned, null);
    }

    private static string ValidateOutput(string original, string output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return original;

        output = output.Replace("```", "").Replace("**", "").Trim();
        var looksLikeReasoning = output.Contains("We are given", StringComparison.OrdinalIgnoreCase)
            || output.Contains("Step 1", StringComparison.OrdinalIgnoreCase)
            || output.Contains("Let me", StringComparison.OrdinalIgnoreCase)
            || output.Contains("sentence:", StringComparison.OrdinalIgnoreCase)
            || output.Contains("The corrected", StringComparison.OrdinalIgnoreCase)
            || output.Contains("Original:", StringComparison.OrdinalIgnoreCase)
            || output.Contains("Correction:", StringComparison.OrdinalIgnoreCase)
            || output.Contains('\n');

        if (!looksLikeReasoning && output.Length <= original.Length * 2.5)
            return CleanFinalOutput(original, output);

        var extractionPatterns = new[]
        {
            @"[Cc]orrect(?:ion|ed\s+text)\s*(?:is|:)\s*""([^""]+)""",
            @"[Cc]orrect(?:ion|ed)\s*:\s*""([^""]+)""",
            @"[Oo]utput\s*:\s*""([^""]+)""",
            @"[Rr]esult\s*:\s*""([^""]+)""",
            @"[Ff]inal\s*(?:text|answer|version|output)\s*(?:is|:)\s*""([^""]+)""",
            @"[Ss]hould\s+be\s*:?\s*""([^""]+)""",
            @"[Ss]entence\s*:\s*""([^""]+)"""
        };

        foreach (var pattern in extractionPatterns)
        {
            var opts = pattern.Contains("entence") ? RegexOptions.RightToLeft : RegexOptions.None;
            var match = Regex.Match(output, pattern, opts);
            if (match.Success && match.Groups[1].Value.Length > 0)
            {
                var extracted = match.Groups[1].Value.Trim();
                if (extracted.Length >= original.Length * 0.3
                    && extracted.Length <= original.Length * 3
                    && !extracted.Equals(original, StringComparison.OrdinalIgnoreCase))
                {
                    return extracted;
                }
            }
        }

        var quotedMatches = Regex.Matches(output, @"""([^""]{3,})""");
        string? bestQuoted = null;
        foreach (Match m in quotedMatches)
        {
            var q = m.Groups[1].Value.Trim();
            if (q.Length >= original.Length * 0.5 && q.Length <= original.Length * 2 && q != original)
                bestQuoted = q;
        }

        if (bestQuoted != null)
            return bestQuoted;

        var firstLine = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)[0].Trim();
        if (firstLine.Length <= original.Length * 2.5
            && firstLine.Length >= original.Length * 0.3
            && !looksLikeReasoning)
        {
            return CleanFinalOutput(original, firstLine);
        }

        return original;
    }

    private static string CleanFinalOutput(string original, string output)
    {
        output = SanitizeCandidateOutput(output);
        if (output.Length > original.Length * 2.5)
            return original;
        if (output.Length < original.Length * 0.2)
            return original;
        return output;
    }

    private static string SanitizeCandidateOutput(string output)
    {
        output = output.Replace("```", "").Replace("**", "").Trim();

        var prefixes = new[]
        {
            "Here is the corrected",
            "Here's the corrected",
            "Corrected text:",
            "Corrected:",
            "The corrected",
            "Sure,",
            "Sure!",
            "Of course",
            "I'd be happy",
            "Here you go",
            "The sentence should be"
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

        if (output.StartsWith('"') && output.EndsWith('"') && output.Length > 2)
            output = output[1..^1];

        return output.Trim();
    }

    private static bool LooksLikePlainCorrection(string candidate, string original)
    {
        var cleaned = candidate.Trim();
        if (!cleaned.Contains(' '))
            return false;

        return ValidateCandidateOutput(original, cleaned, CorrectionSafetyMode.Balanced).IsValid;
    }

    private static bool LooksExplanatory(string text)
    {
        var markers = new[]
        {
            "here is",
            "the corrected",
            "original:",
            "correction:",
            "explanation:",
            "step 1",
            "let me",
            "we are given",
            "what this test shows"
        };

        return markers.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static int CountSentences(string text)
    {
        var matches = Regex.Matches(text.Trim(), @"[.!?]+(?:\s|$)");
        return Math.Max(1, matches.Count);
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
public record OllamaResult(
    string CorrectedText,
    bool Success,
    string Message,
    bool ValidationPassed = true,
    bool UsedRetry = false,
    string? RejectedReason = null);

/// <summary>Safety validation result for a model-generated correction.</summary>
public readonly record struct CandidateValidation(
    bool IsValid,
    string SanitizedText,
    string? Reason);

/// <summary>Catalog of models from a configured Ollama endpoint.</summary>
public record OllamaModelCatalog(
    bool Success,
    string Status,
    string Endpoint,
    string ActiveModel,
    IReadOnlyList<string> Models);
