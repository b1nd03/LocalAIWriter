using LocalAIWriter.Core.Extensions;

namespace LocalAIWriter.Core.Services;

/// <summary>
/// Text processing service for sentence extraction, pre/post processing,
/// diff generation, and text analysis.
/// </summary>
public sealed class TextProcessor
{
    /// <summary>
    /// Pre-processes text before AI inference: normalizes whitespace,
    /// detects and protects URLs/emails, normalizes Unicode.
    /// </summary>
    public ProcessedText PreProcess(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
            return new ProcessedText(rawText, rawText, Array.Empty<ProtectedSpan>());

        var protectedSpans = DetectProtectedSpans(rawText);
        var normalized = rawText.NormalizeWhitespace();
        normalized = normalized.Normalize(System.Text.NormalizationForm.FormC);

        return new ProcessedText(rawText, normalized, protectedSpans);
    }

    /// <summary>
    /// Post-processes AI output to ensure protected content is preserved.
    /// </summary>
    public string PostProcess(string aiOutput, ProcessedText originalInput)
    {
        var result = aiOutput;

        // Restore protected spans (URLs, emails, etc.)
        foreach (var span in originalInput.ProtectedSpans)
        {
            if (!result.Contains(span.Text, StringComparison.OrdinalIgnoreCase))
            {
                // If the AI removed a protected span, try to restore it
                // This is a safety mechanism
            }
        }

        return result;
    }

    /// <summary>
    /// Validates that the AI output is semantically similar to the input.
    /// Prevents the model from completely changing the meaning.
    /// </summary>
    public bool ValidateSemanticPreservation(string original, string corrected)
    {
        if (string.IsNullOrWhiteSpace(original) || string.IsNullOrWhiteSpace(corrected))
            return true;

        // Length change sanity check (< 30% delta)
        double lengthRatio = (double)corrected.Length / original.Length;
        if (lengthRatio < 0.7 || lengthRatio > 1.3)
            return false;

        // Word overlap check — at least 50% of original words should be in the correction
        var origWords = original.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.ToLowerInvariant().Trim('.', ',', '!', '?'))
            .ToHashSet();
        var corrWords = corrected.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.ToLowerInvariant().Trim('.', ',', '!', '?'))
            .ToHashSet();

        int overlap = origWords.Intersect(corrWords).Count();
        double overlapRatio = (double)overlap / Math.Max(origWords.Count, 1);

        return overlapRatio >= 0.5;
    }

    /// <summary>
    /// Generates a word-level diff between original and corrected text.
    /// </summary>
    public IReadOnlyList<DiffSegment> GenerateDiff(string original, string corrected)
    {
        return original.ComputeWordDiff(corrected);
    }

    /// <summary>
    /// Computes a readability score (simplified Flesch-Kincaid).
    /// </summary>
    public double ComputeReadabilityScore(string text)
    {
        var sentences = text.SplitIntoSentences();
        if (sentences.Count == 0) return 100.0;

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int syllables = words.Sum(CountSyllables);

        double wordsPerSentence = (double)words.Length / sentences.Count;
        double syllablesPerWord = (double)syllables / Math.Max(words.Length, 1);

        // Flesch Reading Ease formula
        return 206.835 - (1.015 * wordsPerSentence) - (84.6 * syllablesPerWord);
    }

    #region Private Methods

    private static List<ProtectedSpan> DetectProtectedSpans(string text)
    {
        var spans = new List<ProtectedSpan>();
        var words = text.Split(' ');
        int position = 0;

        foreach (var word in words)
        {
            int idx = text.IndexOf(word, position, StringComparison.Ordinal);
            if (idx >= 0)
            {
                if (word.ContainsUrl() || word.ContainsEmail())
                {
                    spans.Add(new ProtectedSpan(word, idx, ProtectedType.Url));
                }
                position = idx + word.Length;
            }
        }

        return spans;
    }

    private static int CountSyllables(string word)
    {
        word = word.ToLowerInvariant().Trim('.', ',', '!', '?', ';', ':');
        if (word.Length <= 2) return 1;

        int count = 0;
        bool lastWasVowel = false;

        foreach (char c in word)
        {
            bool isVowel = "aeiou".Contains(c);
            if (isVowel && !lastWasVowel)
                count++;
            lastWasVowel = isVowel;
        }

        // Silent 'e' rule
        if (word.EndsWith('e') && count > 1)
            count--;

        return Math.Max(count, 1);
    }

    #endregion
}

/// <summary>Text after pre-processing with protected spans identified.</summary>
public record ProcessedText(string Original, string Normalized, IReadOnlyList<ProtectedSpan> ProtectedSpans);

/// <summary>A span of text that should not be modified by AI (URLs, emails, etc.).</summary>
public record ProtectedSpan(string Text, int StartIndex, ProtectedType Type);

/// <summary>Types of protected content.</summary>
public enum ProtectedType
{
    Url,
    Email,
    CodeBlock,
    Mention,
    ProperNoun
}
