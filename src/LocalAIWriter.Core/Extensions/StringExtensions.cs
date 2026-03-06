namespace LocalAIWriter.Core.Extensions;

/// <summary>
/// High-performance string extension methods for text processing.
/// </summary>
public static class StringExtensions
{
    /// <summary>
    /// Detects sentence boundaries and returns individual sentences.
    /// </summary>
    public static IReadOnlyList<string> SplitIntoSentences(this string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<string>();

        var sentences = new List<string>();
        var current = new System.Text.StringBuilder(text.Length);

        for (int i = 0; i < text.Length; i++)
        {
            current.Append(text[i]);

            if (IsSentenceEnding(text, i))
            {
                var sentence = current.ToString().Trim();
                if (sentence.Length > 0)
                    sentences.Add(sentence);
                current.Clear();
            }
        }

        var remaining = current.ToString().Trim();
        if (remaining.Length > 0)
            sentences.Add(remaining);

        return sentences;
    }

    /// <summary>
    /// Normalizes whitespace: collapses multiple spaces, trims, normalizes line endings.
    /// </summary>
    public static string NormalizeWhitespace(this string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var sb = new System.Text.StringBuilder(text.Length);
        bool lastWasSpace = false;

        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace)
                {
                    sb.Append(' ');
                    lastWasSpace = true;
                }
            }
            else
            {
                sb.Append(c);
                lastWasSpace = false;
            }
        }

        return sb.ToString().Trim();
    }

    /// <summary>
    /// Capitalizes the first letter of a string.
    /// </summary>
    public static string CapitalizeFirst(this string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        if (char.IsUpper(text[0]))
            return text;

        return string.Create(text.Length, text, static (span, str) =>
        {
            str.AsSpan().CopyTo(span);
            span[0] = char.ToUpper(span[0]);
        });
    }

    /// <summary>
    /// Checks if a string contains a URL pattern.
    /// </summary>
    public static bool ContainsUrl(this string text)
    {
        return text.Contains("http://", StringComparison.OrdinalIgnoreCase)
            || text.Contains("https://", StringComparison.OrdinalIgnoreCase)
            || text.Contains("www.", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks if a string contains an email pattern.
    /// </summary>
    public static bool ContainsEmail(this string text)
    {
        int atIndex = text.IndexOf('@');
        return atIndex > 0 && atIndex < text.Length - 1 && text.IndexOf('.', atIndex) > atIndex;
    }

    /// <summary>
    /// Computes a simple word-level diff between original and corrected text.
    /// </summary>
    public static IReadOnlyList<DiffSegment> ComputeWordDiff(this string original, string corrected)
    {
        var origWords = original.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var corrWords = corrected.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var result = new List<DiffSegment>();

        int i = 0, j = 0;
        while (i < origWords.Length && j < corrWords.Length)
        {
            if (string.Equals(origWords[i], corrWords[j], StringComparison.Ordinal))
            {
                result.Add(new DiffSegment(origWords[i], DiffType.Unchanged));
                i++; j++;
            }
            else
            {
                result.Add(new DiffSegment(origWords[i], DiffType.Removed));
                result.Add(new DiffSegment(corrWords[j], DiffType.Added));
                i++; j++;
            }
        }

        while (i < origWords.Length)
        {
            result.Add(new DiffSegment(origWords[i], DiffType.Removed));
            i++;
        }

        while (j < corrWords.Length)
        {
            result.Add(new DiffSegment(corrWords[j], DiffType.Added));
            j++;
        }

        return result;
    }

    #region Private Helpers

    private static bool IsSentenceEnding(string text, int index)
    {
        char c = text[index];
        if (c != '.' && c != '!' && c != '?')
            return false;

        // Check it's not an abbreviation (e.g., "Dr.", "Mr.", "U.S.")
        if (c == '.' && index > 0 && index < text.Length - 1)
        {
            // If the next char is a letter without a space, it's likely an abbreviation
            if (index + 1 < text.Length && char.IsLetter(text[index + 1]))
                return false;

            // Single-letter abbreviation (e.g., "U.S.A.")
            if (index >= 2 && text[index - 1] == '.' )
                return false;
        }

        return true;
    }

    #endregion
}

/// <summary>
/// Represents a segment in a text diff.
/// </summary>
public record DiffSegment(string Text, DiffType Type);

/// <summary>
/// Type of diff change.
/// </summary>
public enum DiffType
{
    Unchanged,
    Added,
    Removed
}
