using System.Buffers;

namespace LocalAIWriter.Core.Extensions;

/// <summary>
/// High-performance Span-based string processing extensions
/// for zero-allocation text manipulation in hot paths.
/// </summary>
public static class SpanExtensions
{
    /// <summary>
    /// Counts words in a span without allocating.
    /// </summary>
    public static int CountWords(this ReadOnlySpan<char> text)
    {
        if (text.IsEmpty)
            return 0;

        int count = 0;
        bool inWord = false;

        for (int i = 0; i < text.Length; i++)
        {
            if (char.IsWhiteSpace(text[i]))
            {
                inWord = false;
            }
            else if (!inWord)
            {
                inWord = true;
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Checks if a span contains only ASCII characters.
    /// </summary>
    public static bool IsAsciiOnly(this ReadOnlySpan<char> text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] > 127)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Checks if the text has any uppercase letters after the first character.
    /// Useful for detecting ALL CAPS or camelCase.
    /// </summary>
    public static bool HasMixedCase(this ReadOnlySpan<char> text)
    {
        bool hasUpper = false, hasLower = false;
        for (int i = 0; i < text.Length; i++)
        {
            if (char.IsUpper(text[i])) hasUpper = true;
            if (char.IsLower(text[i])) hasLower = true;
            if (hasUpper && hasLower) return true;
        }
        return false;
    }

    /// <summary>
    /// Finds the index of the first sentence-ending punctuation mark.
    /// </summary>
    public static int IndexOfSentenceEnd(this ReadOnlySpan<char> text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '.' || text[i] == '!' || text[i] == '?')
            {
                // Verify it's followed by whitespace or end of text
                if (i + 1 >= text.Length || char.IsWhiteSpace(text[i + 1]))
                    return i;
            }
        }
        return -1;
    }

    /// <summary>
    /// Trims whitespace on both sides without allocating a new string until ToString.
    /// </summary>
    public static ReadOnlySpan<char> TrimSpan(this ReadOnlySpan<char> text)
    {
        return text.Trim();
    }

    /// <summary>
    /// Copies source span to a rented array for temporary use.
    /// Caller MUST return the array via ArrayPool.
    /// </summary>
    public static char[] ToPooledArray(this ReadOnlySpan<char> text, ArrayPool<char> pool)
    {
        var array = pool.Rent(text.Length);
        text.CopyTo(array);
        return array;
    }
}
