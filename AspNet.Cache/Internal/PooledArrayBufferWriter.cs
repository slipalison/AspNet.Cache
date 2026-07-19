using System;
using System.Buffers;

namespace AspNet.Cache.Internal;

/// <summary>
/// <see cref="IBufferWriter{T}"/> backed by <see cref="ArrayPool{T}.Shared"/>. Single-threaded, per-request use only.
/// </summary>
internal sealed class PooledArrayBufferWriter : IBufferWriter<byte>, IDisposable
{
    private const int DefaultInitialCapacity = 4096;

    private byte[] _buffer;
    private int _written;
    private readonly int _softLimit;

    public PooledArrayBufferWriter(int softLimit = int.MaxValue, int initialCapacity = DefaultInitialCapacity)
    {
        _buffer = ArrayPool<byte>.Shared.Rent(initialCapacity);
        _softLimit = softLimit;
    }

    /// <summary>True when more than the configured soft limit has been written; the caller must discard the content.</summary>
    public bool Overflowed => _written > _softLimit;

    public ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, _written);

    public ReadOnlyMemory<byte> WrittenMemory => _buffer.AsMemory(0, _written);

    public int WrittenCount => _written;

    public void Advance(int count)
    {
        if (count < 0 || _written + count > _buffer.Length)
            throw new ArgumentOutOfRangeException(nameof(count));
        _written += count;
    }

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        EnsureCapacity(sizeHint);
        return _buffer.AsMemory(_written);
    }

    public Span<byte> GetSpan(int sizeHint = 0)
    {
        EnsureCapacity(sizeHint);
        return _buffer.AsSpan(_written);
    }

    public void Dispose()
    {
        var buffer = _buffer;
        _buffer = Array.Empty<byte>();
        _written = 0;
        if (buffer.Length > 0)
            ArrayPool<byte>.Shared.Return(buffer);
    }

    private void EnsureCapacity(int sizeHint)
    {
        if (sizeHint < 1)
            sizeHint = 1;
        if (_buffer.Length - _written >= sizeHint)
            return;

        var newSize = Math.Max(_buffer.Length * 2, _written + sizeHint);
        var next = ArrayPool<byte>.Shared.Rent(newSize);
        _buffer.AsSpan(0, _written).CopyTo(next);
        var previous = _buffer;
        _buffer = next;
        ArrayPool<byte>.Shared.Return(previous);
    }
}
