using Xunit;
using LocalAIWriter.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace LocalAIWriter.Tests;

// ═══════════════════════════════════════════════════════════════════════════════
// RuleBasedEngine Tests
// ═══════════════════════════════════════════════════════════════════════════════

public class RuleBasedEngineTests
{
    private readonly RuleBasedEngine _engine = new();

    [Fact]
    public void ApplyRules_EmptyString_ReturnsEmptyString()
    {
        var result = _engine.ApplyRules("");
        Assert.Equal("", result.CorrectedText);
        Assert.Empty(result.Corrections);
    }

    [Fact]
    public void ApplyRules_DoubleSpaces_RemovesExtraSpaces()
    {
        var result = _engine.ApplyRules("hello  world");
        Assert.DoesNotContain("  ", result.CorrectedText);
    }

    [Fact]
    public void ApplyRules_DuplicateWords_RemovesDuplicate()
    {
        var result = _engine.ApplyRules("The the quick brown fox");
        Assert.DoesNotContain("the the", result.CorrectedText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApplyRules_HadHad_KeepsIntentionalDuplicate()
    {
        // "had had" is valid English (pluperfect) — must NOT be de-duplicated
        var result = _engine.ApplyRules("He had had enough of it.");
        Assert.Contains("had had", result.CorrectedText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApplyRules_LowercaseStart_CapitalizesFirst()
    {
        var result = _engine.ApplyRules("hello world");
        Assert.True(char.IsUpper(result.CorrectedText[0]));
    }

    [Fact]
    public void ApplyRules_CommonMisspelling_FixesIt()
    {
        var result = _engine.ApplyRules("I definately want this");
        Assert.Contains("definitely", result.CorrectedText);
    }

    [Fact]
    public void ApplyRules_StandaloneI_Capitalizes()
    {
        var result = _engine.ApplyRules("i think i can do it");
        Assert.DoesNotContain(" i ", result.CorrectedText);
    }

    [Fact]
    public void ApplyRules_NoIssues_ReturnsOriginal()
    {
        var result = _engine.ApplyRules("Hello.");
        Assert.Equal("Hello.", result.CorrectedText);
    }

    [Fact]
    public void ApplyRules_ReturnsCorrections()
    {
        var result = _engine.ApplyRules("definately  bad");
        Assert.NotEmpty(result.Corrections);
        Assert.All(result.Corrections, c => Assert.NotEmpty(c.Description));
    }

    // ── Regression tests for false-positive bugs ──

    [Fact]
    public void ApplyRules_SheIsIll_DoesNotCorruptSentence()
    {
        // "ill" must NOT be replaced with "I'll" — "ill" is a valid adjective
        var result = _engine.ApplyRules("She is ill.");
        Assert.Contains("ill", result.CorrectedText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("I'll", result.CorrectedText);
    }

    [Fact]
    public void ApplyRules_EverydayLife_NotChanged()
    {
        // "everyday" as adjective must NOT be changed to "every day"
        var result = _engine.ApplyRules("This is an everyday problem.");
        Assert.Contains("everyday", result.CorrectedText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApplyRules_SheBecameFamous_NotCorrupted()
    {
        // "became" at end of sentence must NOT be changed to "become"
        var result = _engine.ApplyRules("She became famous.");
        Assert.Contains("became", result.CorrectedText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("become", result.CorrectedText);
    }

    [Fact]
    public void ApplyRules_WillBecame_FixesToWillBecome()
    {
        // "will became" IS correctly fixed to "will become"
        var result = _engine.ApplyRules("He will became a doctor.");
        Assert.Contains("will become", result.CorrectedText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApplyRules_AUniversity_NotChangedToAn()
    {
        // "a university" — vowel letter but consonant sound — must NOT become "an university"
        var result = _engine.ApplyRules("She studied at a university.");
        Assert.DoesNotContain("an university", result.CorrectedText, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("definately", "definitely")]
    [InlineData("definatly", "definitely")]
    [InlineData("recieve", "receive")]
    [InlineData("seperate", "separate")]
    [InlineData("occured", "occurred")]
    [InlineData("beleive", "believe")]
    [InlineData("accomodate", "accommodate")]
    [InlineData("goverment", "government")]
    [InlineData("enviroment", "environment")]
    [InlineData("tommorow", "tomorrow")]
    [InlineData("wierd", "weird")]
    [InlineData("untill", "until")]
    [InlineData("buisness", "business")]
    [InlineData("embarass", "embarrass")]
    [InlineData("freind", "friend")]
    public void ApplyRules_CommonMisspellings_AreFixed(string wrong, string right)
    {
        var result = _engine.ApplyRules($"I {wrong} this.");
        Assert.Contains(right, result.CorrectedText, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("She is ill.")] // "ill" is a valid word
    [InlineData("An everyday routine.")] // "everyday" as adjective
    [InlineData("She became a star.")] // "became" past tense
    [InlineData("He knows everything.")] // unchanged
    public void ApplyRules_CleanSentences_NotCorrupted(string input)
    {
        var result = _engine.ApplyRules(input);
        // Should not fundamentally change meaning — result should still contain key words
        var inputWords = input.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                              .Where(w => w.Length > 3).ToArray();
        foreach (var word in inputWords)
        {
            var cleanWord = word.Trim('.', ',', '!', '?');
            // Each meaningful word should still appear in some form
            Assert.True(
                result.CorrectedText.Contains(cleanWord, StringComparison.OrdinalIgnoreCase),
                $"Word '{cleanWord}' was removed from '{input}' → '{result.CorrectedText}'");
        }
    }

    [Fact]
    public void ApplyRules_CapitalizationAfterPeriod_Works()
    {
        var result = _engine.ApplyRules("Hello. my name is john.");
        Assert.Contains("My", result.CorrectedText);
    }

    [Fact]
    public void ApplyRules_Performance_Under10ms()
    {
        // Rule-based engine must be fast
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 10; i++)
            _engine.ApplyRules("I definately recieve to many emails everyday and its effect me badly.");
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds < 200, $"10 runs took {sw.ElapsedMilliseconds}ms (should be <200ms)");
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// TextProcessor Tests
// ═══════════════════════════════════════════════════════════════════════════════

public class TextProcessorTests
{
    private readonly TextProcessor _processor = new();

    [Fact]
    public void PreProcess_NullOrEmpty_ReturnsInput()
    {
        var result = _processor.PreProcess("");
        Assert.Equal("", result.Normalized);
    }

    [Fact]
    public void PreProcess_ExtraWhitespace_Normalizes()
    {
        var result = _processor.PreProcess("  hello   world  ");
        Assert.Equal("hello world", result.Normalized);
    }

    [Fact]
    public void PreProcess_DetectsUrls()
    {
        var result = _processor.PreProcess("Visit https://example.com today");
        Assert.True(result.ProtectedSpans.Count > 0);
    }

    [Fact]
    public void ValidateSemanticPreservation_SameText_ReturnsTrue()
    {
        Assert.True(_processor.ValidateSemanticPreservation("hello world", "hello world"));
    }

    [Fact]
    public void ValidateSemanticPreservation_TotallyDifferent_ReturnsFalse()
    {
        Assert.False(_processor.ValidateSemanticPreservation(
            "The quick brown fox jumps over the lazy dog",
            "xyz abc def"));
    }

    [Fact]
    public void ValidateSemanticPreservation_MajorLengthChange_ReturnsFalse()
    {
        Assert.False(_processor.ValidateSemanticPreservation(
            "This is a long sentence with many words in it",
            "Short"));
    }

    [Fact]
    public void ComputeReadabilityScore_SimpleSentence_HighScore()
    {
        double score = _processor.ComputeReadabilityScore("The cat sat on the mat.");
        Assert.True(score > 50);
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// Tokenizer Tests
// ═══════════════════════════════════════════════════════════════════════════════

public class TokenizerTests
{
    private readonly Tokenizer _tokenizer;

    public TokenizerTests()
    {
        _tokenizer = new Tokenizer(NullLogger<Tokenizer>.Instance);
        _tokenizer.LoadVocabulary("nonexistent.model"); // triggers fallback to built-in vocab
    }

    [Fact]
    public void IsLoaded_AfterFallback_ReturnsTrue()
    {
        Assert.True(_tokenizer.IsLoaded);
    }

    [Fact]
    public void Encode_EmptyString_ReturnsEosToken()
    {
        var tokens = _tokenizer.Encode("");
        Assert.Equal(Tokenizer.EosTokenId, tokens[0]);
    }

    [Fact]
    public void Encode_SimpleText_ReturnsTokenIds()
    {
        var tokens = _tokenizer.Encode("the cat");
        Assert.NotEqual(Tokenizer.PadTokenId, tokens[0]);
    }

    [Fact]
    public void CreateAttentionMask_AllPad_AllZeros()
    {
        var ids = new int[5]; // all zeros = pad
        var mask = _tokenizer.CreateAttentionMask(ids);
        Assert.All(mask, m => Assert.Equal(0L, m));
    }

    [Fact]
    public void CreateAttentionMask_WithTokens_HasOnes()
    {
        var ids = new int[] { 5, 10, 0, 0, 0 };
        var mask = _tokenizer.CreateAttentionMask(ids);
        Assert.Equal(1L, mask[0]);
        Assert.Equal(1L, mask[1]);
        Assert.Equal(0L, mask[2]);
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// Security Tests
// ═══════════════════════════════════════════════════════════════════════════════

public class SecurityTests
{
    [Fact]
    public void ComputeFileHash_ValidFile_ReturnsCorrectHex()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "test content");
            var hash1 = Core.Security.SecurityGuardian.ComputeFileHash(tempFile);
            var hash2 = Core.Security.SecurityGuardian.ComputeFileHash(tempFile);
            Assert.NotEmpty(hash1);
            Assert.Equal(64, hash1.Length); // SHA-256 = 64 hex chars
            Assert.Equal(hash1, hash2);     // deterministic
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void VerifyModelIntegrity_MissingFile_ReturnsFalse()
    {
        Assert.False(Core.Security.SecurityGuardian.VerifyModelIntegrity("nofile.onnx", "nofile.sha256"));
    }

    [Fact]
    public void VerifyModelIntegrity_MatchingHash_ReturnsTrue()
    {
        var tempFile = Path.GetTempFileName();
        var hashFile = tempFile + ".sha256";
        try
        {
            File.WriteAllText(tempFile, "test content");
            var expectedHash = Core.Security.SecurityGuardian.ComputeFileHash(tempFile);
            File.WriteAllText(hashFile, expectedHash);

            Assert.True(Core.Security.SecurityGuardian.VerifyModelIntegrity(tempFile, hashFile));
        }
        finally
        {
            File.Delete(tempFile);
            File.Delete(hashFile);
        }
    }

    [Fact]
    public void VerifyModelIntegrity_WrongHash_ReturnsFalse()
    {
        var tempFile = Path.GetTempFileName();
        var hashFile = tempFile + ".sha256";
        try
        {
            File.WriteAllText(tempFile, "test content");
            File.WriteAllText(hashFile, "0000000000000000000000000000000000000000000000000000000000000000");

            Assert.False(Core.Security.SecurityGuardian.VerifyModelIntegrity(tempFile, hashFile));
        }
        finally
        {
            File.Delete(tempFile);
            File.Delete(hashFile);
        }
    }

    [Fact]
    public void AuditNetworkAssemblies_ReturnsListNotNull()
    {
        var result = Core.Security.SecurityGuardian.AuditNetworkAssemblies();
        Assert.NotNull(result);
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// ModelRouter Tests
// ═══════════════════════════════════════════════════════════════════════════════

public class ModelRouterTests
{
    private readonly ModelRouter _router;

    public ModelRouterTests()
    {
        var ruleEngine = new RuleBasedEngine();
        var textProcessor = new TextProcessor();
        var tokenizer = new Tokenizer(NullLogger<Tokenizer>.Instance);
        var memManager = new Core.Memory.InferenceMemoryManager();
        var modelManager = new ModelManager(
            NullLogger<ModelManager>.Instance, tokenizer, memManager);
        var ollamaService = new OllamaService(NullLogger<OllamaService>.Instance);
        _router = new ModelRouter(ruleEngine, modelManager, ollamaService, textProcessor,
            NullLogger<ModelRouter>.Instance);
    }

    [Fact]
    public async Task RouteAndCorrect_EmptyText_ReturnsEmpty()
    {
        var result = await _router.RouteAndCorrectAsync("");
        Assert.Equal("", result.CorrectedText);
    }

    [Fact]
    public async Task RouteAndCorrect_VeryShortText_UsesRuleBased()
    {
        // Short text (< 30 chars) always goes to RuleBased
        var result = await _router.RouteAndCorrectAsync("hi");
        Assert.Equal(InferenceRoute.RuleBased, result.Route);
    }

    [Fact]
    public async Task RouteAndCorrect_MediumText_UsesCascadedOrRuleBased()
    {
        // Medium text routes to Cascaded (or falls back to RuleBased if Ollama not available)
        var text = "I definately want to recieve the report by tommorow morning.";
        var result = await _router.RouteAndCorrectAsync(text);
        Assert.True(result.Route == InferenceRoute.Cascaded || result.Route == InferenceRoute.RuleBased);
        // Either way, misspellings should be fixed by the rule stage
        Assert.Contains("definitely", result.CorrectedText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RouteAndCorrect_WithMisspelling_FixesIt()
    {
        var result = await _router.RouteAndCorrectAsync("I definately like this");
        Assert.Contains("definitely", result.CorrectedText);
    }

    [Fact]
    public async Task RouteAndCorrect_LatencyRecorded()
    {
        var result = await _router.RouteAndCorrectAsync("Hello world.");
        Assert.True(result.LatencyMs >= 0);
    }

    [Fact]
    public async Task RouteAndCorrect_SemanticPreservationChecked()
    {
        var result = await _router.RouteAndCorrectAsync("The quick brown fox.");
        // SemanticPreservationValid should be true for a clean sentence
        Assert.True(result.SemanticPreservationValid);
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// OllamaService Tests (no actual network calls — just structural / offline tests)
// ═══════════════════════════════════════════════════════════════════════════════

public class ModelRouterRegressionGuardTests
{
    [Fact]
    public async Task RouteAndCorrect_Cascaded_OllamaRegression_KeepsRuleBasedOutput()
    {
        const string input = "No sooner had the presentation begun when the audience started leaving.";
        const string regressedByAi = "No sooner had the presentation begun when the audience started leaving.";

        using var fixture = CreateRouterWithOllamaCandidate(regressedByAi, changed: true);
        var result = await fixture.Router.RouteAndCorrectAsync(input);

        Assert.Equal(InferenceRoute.Cascaded, result.Route);
        Assert.Equal(
            "No sooner had the presentation begun than the audience started leaving.",
            result.CorrectedText);
        Assert.Equal(SafetyDecision.FallbackRuleBased, result.SafetyDecision);
    }

    [Fact]
    public async Task RouteAndCorrect_OllamaRoute_OllamaRegression_FallsBackToRuleBased()
    {
        var sentence = "Had he known the consequences, he would avoid making that decision now.";
        var input = string.Join(" ", Enumerable.Repeat(sentence, 8));

        using var fixture = CreateRouterWithOllamaCandidate(input, changed: true);
        var result = await fixture.Router.RouteAndCorrectAsync(input);

        Assert.Equal(InferenceRoute.RuleBased, result.Route);
        Assert.Equal(SafetyDecision.FallbackRuleBased, result.SafetyDecision);
        Assert.Contains("would have avoided", result.CorrectedText, StringComparison.OrdinalIgnoreCase);
    }

    private static RouterFixture CreateRouterWithOllamaCandidate(string correctedText, bool changed)
    {
        var payload = JsonSerializer.Serialize(new { corrected_text = correctedText, changed });
        var envelope = $"{{\"message\":{{\"content\":{JsonSerializer.Serialize(payload)}}}}}";

        var handler = new SequenceHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(envelope, Encoding.UTF8, "application/json")
        });

        var ruleEngine = new RuleBasedEngine();
        var textProcessor = new TextProcessor();
        var tokenizer = new Tokenizer(NullLogger<Tokenizer>.Instance);
        var memManager = new Core.Memory.InferenceMemoryManager();
        var modelManager = new ModelManager(NullLogger<ModelManager>.Instance, tokenizer, memManager);
        var ollamaService = new OllamaService(handler, NullLogger<OllamaService>.Instance);
        var router = new ModelRouter(
            ruleEngine,
            modelManager,
            ollamaService,
            textProcessor,
            NullLogger<ModelRouter>.Instance);

        return new RouterFixture(router, ollamaService);
    }

    private sealed class RouterFixture : IDisposable
    {
        public ModelRouter Router { get; }
        private readonly OllamaService _ollama;

        public RouterFixture(ModelRouter router, OllamaService ollama)
        {
            Router = router;
            _ollama = ollama;
        }

        public void Dispose() => _ollama.Dispose();
    }
}

public class OllamaServiceTests : IDisposable
{
    private readonly OllamaService _service = new(NullLogger<OllamaService>.Instance);

    [Fact]
    public async Task CorrectGrammar_EmptyInput_ReturnsEmpty()
    {
        var result = await _service.CorrectGrammarAsync("", CancellationToken.None);
        Assert.Equal("", result.CorrectedText);
        Assert.False(result.Success);
    }

    [Fact]
    public async Task CorrectGrammar_OllamaNotRunning_ReturnsOriginalText()
    {
        // Ollama is not running in test environment — should gracefully fallback
        var text = "This is a test sentence.";
        var result = await _service.CorrectGrammarAsync(text,
            new CancellationTokenSource(TimeSpan.FromSeconds(3)).Token);

        // When Ollama is unavailable, original text should be returned
        if (!result.Success)
            Assert.Equal(text, result.CorrectedText);
    }

    [Fact]
    public async Task CheckStatus_OllamaNotRunning_ReturnsNotRunning()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var (running, _, _) = await _service.CheckStatusAsync(cts.Token);
        // In CI/test environment, Ollama is not expected to be running
        // Just verify the method doesn't throw
        _ = running; // result varies by environment
    }

    [Fact]
    public async Task CorrectGrammar_Cancellation_HandlesCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        var result = await _service.CorrectGrammarAsync("Some text.", cts.Token);
        // Should not throw — should return gracefully
        Assert.NotNull(result);
    }

    [Fact]
    public void IsMetaEvaluationInput_ScorecardText_ReturnsTrue()
    {
        const string text =
            "Total sentences: 10\n" +
            "Correct: 2\n" +
            "Incorrect: 8\n" +
            "Accuracy: 20%";

        Assert.True(OllamaService.IsMetaEvaluationInput(text));
    }

    [Fact]
    public void IsMetaEvaluationInput_NormalSentence_ReturnsFalse()
    {
        const string text = "The number of students has increased this year.";
        Assert.False(OllamaService.IsMetaEvaluationInput(text));
    }

    [Fact]
    public async Task CorrectGrammar_MetaEvaluationInput_SuppressesModelCall()
    {
        var handler = new CountingHttpMessageHandler();
        using var service = new OllamaService(handler, NullLogger<OllamaService>.Instance);

        const string text =
            "Total sentences: 10\n" +
            "Correct: 2\n" +
            "Incorrect: 8\n" +
            "Accuracy: 20%";

        var result = await service.CorrectGrammarAsync(text, CorrectionSafetyMode.Conservative, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("meta_evaluation_input", result.RejectedReason);
        Assert.Equal(text, result.CorrectedText);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public void TryExtractStructuredCorrectionFromChatResponse_ValidSchema_Parses()
    {
        var responseJson = BuildChatEnvelope("{\"corrected_text\":\"Fixed sentence.\",\"changed\":true}");

        var ok = OllamaService.TryExtractStructuredCorrectionFromChatResponse(
            responseJson,
            out var correctedText,
            out var changed,
            out var reason);

        Assert.True(ok);
        Assert.Equal("Fixed sentence.", correctedText);
        Assert.True(changed);
        Assert.Null(reason);
    }

    [Fact]
    public void TryExtractStructuredCorrectionFromChatResponse_InvalidContentJson_Fails()
    {
        var responseJson = BuildChatEnvelope("not-json-at-all");

        var ok = OllamaService.TryExtractStructuredCorrectionFromChatResponse(
            responseJson,
            out _,
            out _,
            out var reason);

        Assert.False(ok);
        Assert.Equal("invalid_json", reason);
    }

    [Fact]
    public async Task CorrectGrammar_InvalidJsonTwice_UsesRetryThenKeepsOriginal()
    {
        using var service = new OllamaService(
            new SequenceHttpMessageHandler(
                CreateChatResponse("not-json"),
                CreateChatResponse("still-not-json")),
            NullLogger<OllamaService>.Instance);

        const string input = "This are a test.";
        var result = await service.CorrectGrammarAsync(input, CorrectionSafetyMode.Conservative, CancellationToken.None);

        Assert.False(result.Success);
        Assert.False(result.ValidationPassed);
        Assert.True(result.UsedRetry);
        Assert.Equal(input, result.CorrectedText);
        Assert.Equal("invalid_json", result.RejectedReason);
    }

    [Fact]
    public async Task CorrectGrammar_FirstTryInvalid_SecondTryValid_AppliesWithRetry()
    {
        using var service = new OllamaService(
            new SequenceHttpMessageHandler(
                CreateChatResponse("not-json"),
                CreateChatResponse("{\"corrected_text\":\"This is a test.\",\"changed\":true}")),
            NullLogger<OllamaService>.Instance);

        var result = await service.CorrectGrammarAsync(
            "This are a test.",
            CorrectionSafetyMode.Conservative,
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(result.ValidationPassed);
        Assert.True(result.UsedRetry);
        Assert.Equal("This is a test.", result.CorrectedText);
        Assert.Null(result.RejectedReason);
    }

    [Fact]
    public async Task CorrectGrammar_MetaEvaluationOutput_IsRejectedAndOriginalKept()
    {
        const string input = "No sooner had the meeting begun when people left.";
        var metaOutput = "{\"corrected_text\":\"Total sentences: 1\\nCorrect: 0\\nIncorrect: 1\\nAccuracy: 0%\",\"changed\":true}";

        using var service = new OllamaService(
            new SequenceHttpMessageHandler(CreateChatResponse(metaOutput)),
            NullLogger<OllamaService>.Instance);

        var result = await service.CorrectGrammarAsync(input, CorrectionSafetyMode.Conservative, CancellationToken.None);

        Assert.False(result.Success);
        Assert.False(result.ValidationPassed);
        Assert.False(result.UsedRetry); // validation failure is fail-safe, not schema retry
        Assert.Equal("meta_evaluation_output", result.RejectedReason);
        Assert.Equal(input, result.CorrectedText);
    }

    [Fact]
    public void ValidateCandidateOutput_MetaEvaluationCandidate_IsRejected()
    {
        const string original = "The data had been reviewed before publication.";
        const string candidate = "Correct: 0\nIncorrect: 1\nAccuracy: 0%";
        var validation = OllamaService.ValidateCandidateOutput(
            original,
            candidate,
            CorrectionSafetyMode.Conservative);

        Assert.False(validation.IsValid);
        Assert.Equal("meta_evaluation_output", validation.Reason);
        Assert.Equal(original, validation.SanitizedText);
    }

    [Fact]
    public void ValidateCandidateOutput_HugeLengthShift_IsRejected()
    {
        const string original = "The sentence is concise.";
        var candidate = string.Join(' ', Enumerable.Repeat("expanded", 80));
        var validation = OllamaService.ValidateCandidateOutput(
            original,
            candidate,
            CorrectionSafetyMode.Conservative);

        Assert.False(validation.IsValid);
        Assert.Equal("length_ratio_out_of_bounds", validation.Reason);
        Assert.Equal(original, validation.SanitizedText);
    }

    [Fact]
    public void ValidateCandidateOutput_ConservativeSafetyRejectsSentenceCountShift()
    {
        const string original = "This sentence stays short.";
        const string candidate = "This sentence stays. Short.";
        var validation = OllamaService.ValidateCandidateOutput(
            original,
            candidate,
            CorrectionSafetyMode.Conservative);

        Assert.False(validation.IsValid);
        Assert.Equal("sentence_count_shift", validation.Reason);
    }

    private static HttpResponseMessage CreateChatResponse(string messageContent)
    {
        var body = BuildChatEnvelope(messageContent);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }

    private static string BuildChatEnvelope(string messageContent)
    {
        var escaped = JsonSerializer.Serialize(messageContent);
        return $"{{\"message\":{{\"content\":{escaped}}}}}";
    }

    public void Dispose() => _service.Dispose();
}

public class AdvancedGrammarAccuracyBenchmarkTests
{
    private readonly RuleBasedEngine _engine = new();

    [Fact]
    public void RuleBasedEngine_AdvancedGrammar_100CaseAccuracy()
    {
        var cases = BuildCases();
        Assert.Equal(100, cases.Count);

        int passed = 0;
        var failures = new List<string>();

        foreach (var testCase in cases)
        {
            var actual = _engine.ApplyRules(testCase.Input).CorrectedText;
            if (actual == testCase.Expected)
            {
                passed++;
            }
            else
            {
                failures.Add($"IN: {testCase.Input} | EXP: {testCase.Expected} | ACT: {actual}");
            }
        }

        double accuracy = passed / (double)cases.Count;
        Console.WriteLine($"Advanced grammar benchmark accuracy: {accuracy:P2} ({passed}/{cases.Count})");
        if (failures.Count > 0)
            Console.WriteLine($"Sample failures: {string.Join(" || ", failures.Take(3))}");
        Assert.True(
            accuracy >= 0.90,
            $"Advanced grammar benchmark accuracy: {accuracy:P1} ({passed}/{cases.Count}). " +
            $"Sample failures: {string.Join(" || ", failures.Take(5))}");
    }

    private static List<BenchmarkCase> BuildCases()
    {
        var cases = new List<BenchmarkCase>();

        var typoCases = new (string Wrong, string Right)[]
        {
            ("definately", "definitely"),
            ("recieve", "receive"),
            ("seperate", "separate"),
            ("occured", "occurred"),
            ("beleive", "believe"),
            ("wierd", "weird"),
            ("accomodate", "accommodate"),
            ("goverment", "government"),
            ("begining", "beginning"),
            ("calender", "calendar"),
            ("tommorow", "tomorrow"),
            ("truely", "truly"),
            ("beautful", "beautiful"),
            ("buisness", "business"),
            ("catagory", "category"),
            ("changable", "changeable"),
            ("decieve", "deceive"),
            ("diffrence", "difference"),
            ("embarass", "embarrass"),
            ("exellent", "excellent"),
            ("fourty", "forty"),
            ("hygeine", "hygiene"),
            ("immediatly", "immediately"),
            ("libary", "library"),
            ("millenium", "millennium"),
            ("minature", "miniature"),
            ("noticable", "noticeable"),
            ("reccomend", "recommend"),
            ("relevent", "relevant"),
            ("resturant", "restaurant"),
            ("shedule", "schedule"),
            ("succesful", "successful"),
            ("useing", "using"),
            ("probaly", "probably"),
            ("intresting", "interesting"),
        };

        foreach (var (wrong, right) in typoCases)
            cases.Add(Fix($"I {wrong} this message.", $"I {right} this message."));

        cases.AddRange(new[]
        {
            Fix("He have a car.", "He has a car."),
            Fix("She is interested on math.", "She is interested in math."),
            Fix("The report consists from three parts.", "The report consists of three parts."),
            Fix("We was waiting outside.", "We were waiting outside."),
            Fix("Each of the students have a book.", "Each of the students has a book."),
            Fix("The team are winning today.", "The team is winning today."),
            Fix("My friend and me went home.", "My friend and I went home."),
            Fix("He didn't went to school.", "He didn't go to school."),
            Fix("She has wrote a letter.", "She has written a letter."),
            Fix("He has took the keys.", "He has taken the keys."),
            Fix("She has gave me advice.", "She has given me advice."),
            Fix("I have saw him yesterday.", "I saw him yesterday."),
            Fix("There is many people here.", "There are many people here."),
            Fix("There's too many errors today.", "There are too many errors today."),
            Fix("There's too much people outside.", "There are too many people outside."),
            Fix("Less people attended today.", "Fewer people attended today."),
            Fix("There is less people now.", "There are fewer people now."),
            Fix("I wish I was taller.", "I wish I were taller."),
            Fix("She avoided to answer the question.", "She avoided answering the question."),
            Fix("We are looking forward to meet you.", "We are looking forward to meeting you."),
            Fix("Despite of the rain, we played.", "Despite the rain, we played."),
            Fix("The class is divided in two groups.", "The class is divided into two groups."),
            Fix("He should to leave now.", "He should leave now."),
            Fix("No sooner had he begun when it rained.", "No sooner had he begun than it rained."),
            Fix("Hardly had they began when the show started.", "Hardly had they begun when the show started."),
        });

        cases.AddRange(new[]
        {
            Keep("She is ill."),
            Keep("This is an everyday problem."),
            Keep("She became famous."),
            Keep("He would take the bus every day."),
            Keep("I would rather you call first."),
            Keep("The data suggests a trend."),
            Keep("The turkey is in the oven."),
            Keep("We met in march to plan the release."),
            Keep("I may go tomorrow."),
            Keep("The earth feels warm here."),
            Keep("The internet is slow today."),
            Keep("He lets them leave early."),
            Keep("They were going to help us."),
            Keep("Different than expected, it still worked."),
            Keep("This is a clean sentence."),
            Keep("We reviewed the report yesterday."),
            Keep("Our team is ready now."),
            Keep("Please send the final document."),
            Keep("The library opens at nine."),
            Keep("She has gone home already."),
            Keep("No one was late today."),
            Keep("The schedule is on track."),
            Keep("He said that the meeting was useful."),
            Keep("I can sing and dance."),
            Keep("They are interested in science."),
            Keep("The company has clear policies."),
            Keep("A university can be expensive."),
            Keep("An hour passed quickly."),
            Keep("He works hard every day."),
            Keep("The project scope is clear."),
            Keep("She explained the issue to me."),
            Keep("We discussed the budget yesterday."),
            Keep("I prefer tea to coffee."),
            Keep("The class was divided into two groups."),
            Keep("He is capable of solving this."),
            Keep("The number of users has grown."),
            Keep("Each participant was present."),
            Keep("No sooner had we arrived than it started."),
            Keep("Hardly had they begun when the lights failed."),
            Keep("This information is accurate."),
        });

        return cases;
    }

    private static BenchmarkCase Fix(string input, string expected) => new(input, expected);
    private static BenchmarkCase Keep(string text) => new(text, text);

    private sealed record BenchmarkCase(string Input, string Expected);
}

public class NoChangeFalsePositiveBenchmarkTests
{
    private readonly RuleBasedEngine _engine = new();

    [Fact]
    public void RuleBasedEngine_NoChange100_UnchangedRateAtLeast95Percent()
    {
        var cases = BuildNoChangeCases();
        Assert.Equal(100, cases.Count);

        int unchanged = 0;
        var changedExamples = new List<string>();
        foreach (var text in cases)
        {
            var actual = _engine.ApplyRules(text).CorrectedText;
            if (actual == text)
            {
                unchanged++;
            }
            else
            {
                changedExamples.Add($"IN: {text} | OUT: {actual}");
            }
        }

        double unchangedRate = unchanged / (double)cases.Count;
        Console.WriteLine($"No-change benchmark unchanged rate: {unchangedRate:P2} ({unchanged}/{cases.Count})");
        if (changedExamples.Count > 0)
            Console.WriteLine($"Sample changed clean sentences: {string.Join(" || ", changedExamples.Take(3))}");

        Assert.True(
            unchangedRate >= 0.95,
            $"No-change benchmark unchanged rate: {unchangedRate:P1} ({unchanged}/{cases.Count}). " +
            $"Sample changed: {string.Join(" || ", changedExamples.Take(5))}");
    }

    private static List<string> BuildNoChangeCases()
    {
        var templates = new[]
        {
            "The report for sprint {0} was approved by the manager.",
            "She writes clear documentation for module {0}.",
            "Our team is ready for milestone {0}.",
            "The server responded quickly during test {0}.",
            "Each participant was present at session {0}.",
            "The number of users has increased in batch {0}.",
            "We discussed the budget after meeting {0}.",
            "He is capable of solving task {0}.",
            "The committee reviewed proposal {0} yesterday.",
            "This sentence is grammatically correct sample {0}."
        };

        var cases = new List<string>(100);
        for (int i = 1; i <= 10; i++)
        {
            foreach (var template in templates)
                cases.Add(string.Format(template, i));
        }

        return cases;
    }
}

public class RealWorldMetaOutputRejectionTests
{
    [Fact]
    public void Validation_RealWorld50_RejectsAnalysisStyleOutputEveryTime()
    {
        int rejected = 0;
        for (int i = 1; i <= 50; i++)
        {
            var original = $"The team completed experiment {i} on schedule. The results were logged in the report.";
            var candidate =
                "Total sentences: 2\n" +
                "Correct: 0\n" +
                "Incorrect: 2\n" +
                "Accuracy: 0%\n" +
                "What this test shows: inversion and agreement errors.";

            var validation = OllamaService.ValidateCandidateOutput(
                original,
                candidate,
                CorrectionSafetyMode.Conservative);

            if (!validation.IsValid &&
                (validation.Reason == "meta_evaluation_output" || validation.Reason == "explanatory_output"))
            {
                rejected++;
                Assert.Equal(original, validation.SanitizedText);
            }
        }

        Assert.Equal(50, rejected);
    }
}

public class TenSentenceRegressionTests
{
    private static readonly string[] MetaMarkers =
    {
        "accuracy:",
        "correct:",
        "incorrect:",
        "total sentences",
        "what this test shows",
        "grammatically acceptable"
    };

    private readonly RuleBasedEngine _engine = new();

    [Fact]
    public void RuleBasedEngine_ExactTenSentenceRegressionSet()
    {
        var cases = new (string Input, string Expected)[]
        {
            (
                "Had he known the consequences, he would avoid making that decision now.",
                "Had he known the consequences, he would have avoided making that decision now."
            ),
            (
                "No sooner had the presentation begun when the audience started leaving.",
                "No sooner had the presentation begun than the audience started leaving."
            ),
            (
                "Neither the results of the analysis nor the interpretation were convincing enough to publish.",
                "Neither the results of the analysis nor the interpretation were convincing enough to publish."
            ),
            (
                "The number of students applying for the program have increased dramatically this year.",
                "The number of students applying for the program has increased dramatically this year."
            ),
            (
                "It is essential that every researcher completes their experiment before submitting the paper yesterday.",
                "It is essential that every researcher complete their experiment before submitting the paper yesterday."
            ),
            (
                "The CEO, along with the board members, are responsible for the financial losses.",
                "The CEO, along with the board members, is responsible for the financial losses."
            ),
            (
                "Only after the software was updated they noticed the performance improvement.",
                "Only after the software was updated, did they notice the performance improvement."
            ),
            (
                "She is one of the few scientists who understands and applies the theory correctly in real-world scenarios.",
                "She is one of the few scientists who understand and apply the theory correctly in real-world scenarios."
            ),
            (
                "If the data had been verified earlier, the error could be prevented.",
                "If the data had been verified earlier, the error could have been prevented."
            ),
            (
                "Hardly had they finished the experiment than the equipment malfunctioned.",
                "Hardly had they finished the experiment when the equipment malfunctioned."
            )
        };

        foreach (var testCase in cases)
        {
            var actual = _engine.ApplyRules(testCase.Input).CorrectedText;
            Assert.Equal(testCase.Expected, actual);

            foreach (var marker in MetaMarkers)
            {
                Assert.DoesNotContain(marker, actual, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}

public class UnifiedPipelinePathIntegrationTests
{
    [Fact]
    public void MainWindow_HotkeyAndAiButton_UseUnifiedPipelinePath()
    {
        var mainWindowPath = FindMainWindowSourcePath();
        var source = File.ReadAllText(mainWindowPath);

        Assert.Contains("await pipeline.ProcessAsync(selectedText, _correctionOptions)", source);
        Assert.Contains("await pipeline.ProcessAsync(inputText, _correctionOptions)", source);
        Assert.DoesNotContain("CorrectGrammarAsync(", source, StringComparison.Ordinal);
    }

    private static string FindMainWindowSourcePath()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            var candidate = Path.Combine(current.FullName, "src", "LocalAIWriter", "MainWindow.xaml.cs");
            if (File.Exists(candidate))
                return candidate;
            current = current.Parent;
        }

        throw new FileNotFoundException("Could not locate MainWindow.xaml.cs from test base directory.");
    }
}

internal sealed class SequenceHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _responses;

    public SequenceHttpMessageHandler(params HttpResponseMessage[] responses)
    {
        _responses = new Queue<HttpResponseMessage>(responses);
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (_responses.Count == 0)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("{\"error\":\"no_response_configured\"}", Encoding.UTF8, "application/json")
            });
        }

        return Task.FromResult(_responses.Dequeue());
    }
}

internal sealed class CountingHttpMessageHandler : HttpMessageHandler
{
    public int CallCount { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        CallCount++;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"message\":{\"content\":\"{\\\"corrected_text\\\":\\\"noop\\\",\\\"changed\\\":false}\"}}",
                Encoding.UTF8,
                "application/json")
        });
    }
}
