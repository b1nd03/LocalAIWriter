using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace LocalAIWriter.Core.Security;

/// <summary>
/// Provides secure memory scrubbing to ensure sensitive text data
/// (user text, inference buffers) is zeroed after use and cannot
/// be recovered from memory.
/// </summary>
public static class MemoryScrubber
{
    /// <summary>
    /// Securely zeros a byte span. Uses CryptographicOperations to prevent
    /// the compiler or JIT from optimizing the zeroing away.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void SecureZero(Span<byte> buffer)
    {
        CryptographicOperations.ZeroMemory(buffer);
    }

    /// <summary>
    /// Securely zeros a float array (used for tensor buffers).
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void SecureZero(float[] buffer)
    {
        Array.Clear(buffer, 0, buffer.Length);
        // Volatile write to prevent optimization
        System.Threading.Volatile.Write(ref buffer[0], 0f);
    }

    /// <summary>
    /// Securely zeros an int array (used for token ID buffers).
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void SecureZero(int[] buffer)
    {
        Array.Clear(buffer, 0, buffer.Length);
        System.Threading.Volatile.Write(ref buffer[0], 0);
    }

    /// <summary>
    /// Securely zeros a long array (used for attention masks).
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void SecureZero(long[] buffer)
    {
        Array.Clear(buffer, 0, buffer.Length);
        System.Threading.Volatile.Write(ref buffer[0], 0L);
    }

    /// <summary>
    /// Securely clears a string by overwriting its internal char buffer.
    /// Note: this only works for strings that are pinned or not yet interned.
    /// For maximum safety, prefer using char[] or Span&lt;char&gt; for sensitive data.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static unsafe void SecureZeroString(string value)
    {
        if (string.IsNullOrEmpty(value))
            return;

        fixed (char* ptr = value)
        {
            for (int i = 0; i < value.Length; i++)
            {
                ptr[i] = '\0';
            }
        }
    }
}
