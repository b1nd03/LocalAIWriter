using Microsoft.Extensions.Logging;

namespace LocalAIWriter.Core.Services;

/// <summary>
/// Orchestrates the 5-stage NLP pipeline:
/// Pre-processing → Analysis → Correction → Enhancement → Validation.
/// </summary>
public sealed class NlpPipeline
{
    private readonly TextProcessor _textProcessor;
    private readonly RuleBasedEngine _ruleEngine;
    private readonly ModelRouter _modelRouter;
    private readonly ILogger<NlpPipeline> _logger;

    public NlpPipeline(
        TextProcessor textProcessor,
        RuleBasedEngine ruleEngine,
        ModelRouter modelRouter,
        ILogger<NlpPipeline> logger)
    {
        _textProcessor = textProcessor;
        _ruleEngine = ruleEngine;
        _modelRouter = modelRouter;
        _logger = logger;
    }

    /// <summary>
    /// Executes the full 5-stage NLP pipeline on the input text.
    /// </summary>
    public Task<PipelineResult> ProcessAsync(string rawText, CancellationToken ct = default) =>
        ProcessAsync(rawText, CorrectionOptions.Default, ct);

    /// <summary>
    /// Executes the full 5-stage NLP pipeline on the input text with options.
    /// </summary>
    public async Task<PipelineResult> ProcessAsync(
        string rawText,
        CorrectionOptions options,
        CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Stage 1: Pre-processing
        var preprocessed = _textProcessor.PreProcess(rawText);
        _logger.LogDebug("Stage 1 (Pre-process): {Len} chars, {Protected} protected spans",
            preprocessed.Normalized.Length, preprocessed.ProtectedSpans.Count);

        // Stage 2: Analysis
        var analysis = AnalyzeText(preprocessed.Normalized);
        _logger.LogDebug("Stage 2 (Analysis): readability={Score:F1}, words={Words}",
            analysis.ReadabilityScore, analysis.WordCount);

        // Stage 3: Correction (via model router)
        var routingResult = await _modelRouter.RouteAndCorrectAsync(preprocessed.Normalized, options, ct);
        _logger.LogDebug("Stage 3 (Correction): route={Route}, latency={Ms}ms",
            routingResult.Route, routingResult.LatencyMs);

        // Stage 4: Enhancement (post-process)
        var enhanced = _textProcessor.PostProcess(routingResult.CorrectedText, preprocessed);

        // Stage 5: Validation
        bool isValid = _textProcessor.ValidateSemanticPreservation(rawText, enhanced);
        var safetyDecision = routingResult.SafetyDecision;
        if (!isValid)
        {
            _logger.LogWarning("Stage 5 (Validation): Failed — reverting to original");
            enhanced = rawText;
            safetyDecision = SafetyDecision.RejectedKeepOriginal;
        }

        var diff = _textProcessor.GenerateDiff(rawText, enhanced);

        sw.Stop();
        _logger.LogInformation("Pipeline completed in {Ms}ms via {Route}",
            sw.ElapsedMilliseconds, routingResult.Route);

        return new PipelineResult(
            OriginalText: rawText,
            CorrectedText: enhanced,
            Diff: diff,
            Analysis: analysis,
            Route: routingResult.Route,
            Corrections: routingResult.Corrections,
            TotalLatencyMs: sw.ElapsedMilliseconds,
            IsValid: isValid,
            SafetyDecision: safetyDecision);
    }

    #region Private Methods

    private TextAnalysis AnalyzeText(string text)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var readability = _textProcessor.ComputeReadabilityScore(text);
        var containsCode = text.Contains("```") || text.Contains("    ") || text.Contains('\t');

        return new TextAnalysis(
            WordCount: words.Length,
            ReadabilityScore: readability,
            ContainsCode: containsCode,
            ContainsUrls: text.Contains("http", StringComparison.OrdinalIgnoreCase),
            ContainsEmails: text.Contains('@'));
    }

    #endregion
}

/// <summary>Result from the full NLP pipeline.</summary>
public record PipelineResult(
    string OriginalText,
    string CorrectedText,
    IReadOnlyList<Extensions.DiffSegment> Diff,
    TextAnalysis Analysis,
    InferenceRoute Route,
    IReadOnlyList<RuleCorrection> Corrections,
    long TotalLatencyMs,
    bool IsValid,
    SafetyDecision SafetyDecision);

/// <summary>Text analysis results from Stage 2.</summary>
public record TextAnalysis(
    int WordCount,
    double ReadabilityScore,
    bool ContainsCode,
    bool ContainsUrls,
    bool ContainsEmails);
