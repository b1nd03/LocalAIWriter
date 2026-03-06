using Microsoft.Extensions.Logging;

namespace LocalAIWriter.Core.Services;

/// <summary>
/// Intelligent model routing: analyzes input text complexity and
/// routes to the optimal inference tier (Rule-Based → Ollama → ONNX Model).
/// </summary>
public sealed class ModelRouter
{
    private readonly RuleBasedEngine _ruleEngine;
    private readonly ModelManager _modelManager;
    private readonly OllamaService _ollamaService;
    private readonly TextProcessor _textProcessor;
    private readonly ILogger<ModelRouter> _logger;

    public ModelRouter(
        RuleBasedEngine ruleEngine,
        ModelManager modelManager,
        OllamaService ollamaService,
        TextProcessor textProcessor,
        ILogger<ModelRouter> logger)
    {
        _ruleEngine = ruleEngine;
        _modelManager = modelManager;
        _ollamaService = ollamaService;
        _textProcessor = textProcessor;
        _logger = logger;
    }

    /// <summary>
    /// Routes text to the optimal correction pipeline based on complexity analysis.
    /// </summary>
    public Task<RoutingResult> RouteAndCorrectAsync(string text, CancellationToken ct = default) =>
        RouteAndCorrectAsync(text, CorrectionOptions.Default, ct);

    /// <summary>
    /// Routes text to the optimal correction pipeline based on complexity analysis and options.
    /// </summary>
    public async Task<RoutingResult> RouteAndCorrectAsync(
        string text,
        CorrectionOptions options,
        CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var route = AnalyzeComplexity(text, options);

        _logger.LogDebug("Text routed to {Route}: \"{Preview}...\"",
            route, text.Length > 30 ? text[..30] : text);

        string corrected;
        IReadOnlyList<RuleCorrection> corrections;
        var safetyDecision = SafetyDecision.Applied;

        switch (route)
        {
            case InferenceRoute.RuleBased:
                var ruleResult = _ruleEngine.ApplyRules(text, options.Aggressiveness);
                corrected = ruleResult.CorrectedText;
                corrections = ruleResult.Corrections;
                break;

            case InferenceRoute.Cascaded:
                // Apply rules first, then Ollama for deeper correction
                var rulesFirst = _ruleEngine.ApplyRules(text, options.Aggressiveness);
                var ollamaResult = await _ollamaService.CorrectGrammarAsync(
                    rulesFirst.CorrectedText);
                if (ollamaResult.Success)
                {
                    if (IntroducesDeterministicRegressions(
                            rulesFirst.CorrectedText,
                            ollamaResult.CorrectedText,
                            options.Aggressiveness))
                    {
                        corrected = rulesFirst.CorrectedText;
                        safetyDecision = SafetyDecision.FallbackRuleBased;
                    }
                    else
                    {
                        corrected = ollamaResult.CorrectedText;
                    }
                }
                else
                {
                    corrected = rulesFirst.CorrectedText;
                }
                corrections = rulesFirst.Corrections;
                break;

            case InferenceRoute.OllamaModel:
                var aiResult = await _ollamaService.CorrectGrammarAsync(text);
                if (aiResult.Success)
                {
                    var rulesBaseline = _ruleEngine.ApplyRules(text, options.Aggressiveness);
                    if (IntroducesDeterministicRegressions(
                            rulesBaseline.CorrectedText,
                            aiResult.CorrectedText,
                            options.Aggressiveness))
                    {
                        corrected = rulesBaseline.CorrectedText;
                        corrections = rulesBaseline.Corrections;
                        route = InferenceRoute.RuleBased;
                        safetyDecision = SafetyDecision.FallbackRuleBased;
                        _logger.LogWarning("Ollama output regressed deterministic grammar checks — falling back to rule-based result");
                    }
                    else
                    {
                        corrected = aiResult.CorrectedText;
                        corrections = Array.Empty<RuleCorrection>();
                    }
                }
                else if (!aiResult.Success)
                {
                    corrected = text;
                    corrections = Array.Empty<RuleCorrection>();
                    safetyDecision = SafetyDecision.RejectedKeepOriginal;
                }
                else
                {
                    // Fallback to rules if Ollama not available
                    _logger.LogWarning("Ollama not available — falling back to rule-based: {Msg}", aiResult.Message);
                    var fallback = _ruleEngine.ApplyRules(text, options.Aggressiveness);
                    corrected = fallback.CorrectedText;
                    corrections = fallback.Corrections;
                    route = InferenceRoute.RuleBased;
                    safetyDecision = SafetyDecision.FallbackRuleBased;
                }
                break;

            case InferenceRoute.LightModel:
            case InferenceRoute.HeavyModel:
            default:
                if (_modelManager.IsLoaded)
                {
                    corrected = await _modelManager.ImproveAsync(text, ct);
                    corrections = Array.Empty<RuleCorrection>();
                }
                else
                {
                    // Fallback to rules if model not available
                    var fallback = _ruleEngine.ApplyRules(text, options.Aggressiveness);
                    corrected = fallback.CorrectedText;
                    corrections = fallback.Corrections;
                    route = InferenceRoute.RuleBased;
                    safetyDecision = SafetyDecision.FallbackRuleBased;
                    _logger.LogWarning("Model not available — falling back to rule-based corrections");
                }
                break;
        }

        sw.Stop();

        // Validate semantic preservation
        bool isValid = _textProcessor.ValidateSemanticPreservation(text, corrected);
        if (!isValid)
        {
            _logger.LogWarning("Semantic preservation check failed — keeping original text");
            corrected = text;
            safetyDecision = SafetyDecision.RejectedKeepOriginal;
        }

        return new RoutingResult(
            OriginalText: text,
            CorrectedText: corrected,
            Route: route,
            Corrections: corrections,
            LatencyMs: sw.ElapsedMilliseconds,
            SemanticPreservationValid: isValid,
            SafetyDecision: safetyDecision);
    }

    /// <summary>
    /// Analyzes text complexity to determine the optimal inference route.
    /// Short/simple text → Rule-Based (instant).
    /// Medium text → Cascaded (rules + Ollama).
    /// Long/complex text → Ollama only.
    /// </summary>
    private InferenceRoute AnalyzeComplexity(string text, CorrectionOptions options)
    {
        if (options.Aggressiveness == CorrectionAggressiveness.Low)
            return InferenceRoute.RuleBased;

        // Very short text: rules-only is fast and reliable
        if (text.Length < 30)
            return InferenceRoute.RuleBased;

        bool hasOnlySimpleIssues = HasOnlySimpleIssues(text);
        if (hasOnlySimpleIssues)
            return InferenceRoute.RuleBased;

        // Medium-length prose: cascade rules then Ollama
        if (text.Length < 300)
            return InferenceRoute.Cascaded;

        // Longer text: send directly to Ollama
        return InferenceRoute.OllamaModel;
    }

    private static bool HasOnlySimpleIssues(string text)
    {
        bool hasDoubleSpaces = text.Contains("  ");
        bool needsCapitalization = text.Length > 0 && char.IsLower(text[0]);
        return hasDoubleSpaces || (needsCapitalization && text.Split(' ').Length <= 5);
    }

    private bool IntroducesDeterministicRegressions(
        string baselineText,
        string candidateText,
        CorrectionAggressiveness aggressiveness)
    {
        if (string.Equals(baselineText, candidateText, StringComparison.Ordinal))
            return false;

        int baselineIssues = _ruleEngine.ApplyRules(baselineText, aggressiveness).Corrections.Count;
        int candidateIssues = _ruleEngine.ApplyRules(candidateText, aggressiveness).Corrections.Count;

        if (candidateIssues > baselineIssues)
        {
            _logger.LogDebug(
                "Rejected AI candidate due to deterministic regressions (baselineIssues={Baseline}, candidateIssues={Candidate})",
                baselineIssues,
                candidateIssues);
            return true;
        }

        return false;
    }
}

/// <summary>The inference tier to route text through.</summary>
public enum InferenceRoute
{
    /// <summary>Instant rule-based fixes: capitalization, spacing, misspellings.</summary>
    RuleBased,
    /// <summary>T5-Small grammar correction (less than 200ms).</summary>
    LightModel,
    /// <summary>Larger model for complex rewrites (less than 500ms).</summary>
    HeavyModel,
    /// <summary>Combined: rules first, then Ollama refinement.</summary>
    Cascaded,
    /// <summary>Ollama model for AI-quality grammar correction (configured in settings).</summary>
    OllamaModel
}

/// <summary>Result from the model router.</summary>
public record RoutingResult(
    string OriginalText,
    string CorrectedText,
    InferenceRoute Route,
    IReadOnlyList<RuleCorrection> Corrections,
    long LatencyMs,
    bool SemanticPreservationValid,
    SafetyDecision SafetyDecision);
