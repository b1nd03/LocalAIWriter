using System.Buffers;

namespace LocalAIWriter.Core.Memory;

/// <summary>
/// Custom memory manager that uses ArrayPool and object pooling
/// to achieve near-zero GC pressure during inference hot paths.
/// </summary>
public sealed class InferenceMemoryManager : IDisposable
{
    private readonly ArrayPool<float> _tensorPool;
    private readonly ArrayPool<int> _tokenPool;
    private readonly ArrayPool<long> _longPool;
    private bool _disposed;

    /// <summary>
    /// Initializes the inference memory manager with custom pool sizes.
    /// </summary>
    public InferenceMemoryManager()
    {
        _tensorPool = ArrayPool<float>.Create(maxArrayLength: 65536, maxArraysPerBucket: 4);
        _tokenPool = ArrayPool<int>.Create(maxArrayLength: 256, maxArraysPerBucket: 8);
        _longPool = ArrayPool<long>.Create(maxArrayLength: 256, maxArraysPerBucket: 8);
    }

    /// <summary>
    /// Rent a pre-allocated tensor buffer. MUST return via <see cref="ReturnTensorBuffer"/>.
    /// </summary>
    public float[] RentTensorBuffer(int minLength)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _tensorPool.Rent(minLength);
    }

    /// <summary>
    /// Returns a rented tensor buffer to the pool.
    /// </summary>
    public void ReturnTensorBuffer(float[] buffer, bool clearArray = true)
    {
        if (!_disposed)
            _tensorPool.Return(buffer, clearArray);
    }

    /// <summary>
    /// Rent a pre-allocated token ID buffer. MUST return via <see cref="ReturnTokenBuffer"/>.
    /// </summary>
    public int[] RentTokenBuffer(int minLength)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _tokenPool.Rent(minLength);
    }

    /// <summary>
    /// Returns a rented token buffer to the pool.
    /// </summary>
    public void ReturnTokenBuffer(int[] buffer, bool clearArray = true)
    {
        if (!_disposed)
            _tokenPool.Return(buffer, clearArray);
    }

    /// <summary>
    /// Rent a pre-allocated long buffer (for attention masks, etc.).
    /// MUST return via <see cref="ReturnLongBuffer"/>.
    /// </summary>
    public long[] RentLongBuffer(int minLength)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _longPool.Rent(minLength);
    }

    /// <summary>
    /// Returns a rented long buffer to the pool.
    /// </summary>
    public void ReturnLongBuffer(long[] buffer, bool clearArray = true)
    {
        if (!_disposed)
            _longPool.Return(buffer, clearArray);
    }

    /// <summary>Disposes the memory manager.</summary>
    public void Dispose()
    {
        _disposed = true;
    }
}
