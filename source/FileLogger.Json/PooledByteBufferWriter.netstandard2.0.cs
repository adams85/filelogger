// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

#pragma warning disable CA2201 // Do not raise reserved exception types

namespace Karambolo.Extensions.Logging.File.Json;

// based on: https://github.com/dotnet/runtime/blob/v8.0.22/src/libraries/Common/src/System/Text/Json/PooledByteBufferWriter.cs
internal sealed class PooledByteBufferWriter : IBufferWriter<byte>, IDisposable
{
    // This class allows two possible configurations: if rentedBuffer is not null then
    // it can be used as an IBufferWriter and holds a buffer that should eventually be
    // returned to the shared pool. If rentedBuffer is null, then the instance is in a
    // cleared/disposed state and it must re-rent a buffer before it can be used again.
    private byte[]? _rentedBuffer;
    private int _index;

    private const int MinimumBufferSize = 256;

    // Value copied from Array.MaxLength in System.Private.CoreLib/src/libraries/System.Private.CoreLib/src/System/Array.cs.
    public const int MaximumBufferSize = 0X7FFFFFC7;

    private PooledByteBufferWriter()
    {
#if NETCOREAPP
        // Ensure we are in sync with the Array.MaxLength implementation.
        Debug.Assert(MaximumBufferSize == Array.MaxLength);
#endif
    }

    public PooledByteBufferWriter(int initialCapacity) : this()
    {
        Debug.Assert(initialCapacity > 0);

        _rentedBuffer = ArrayPool<byte>.Shared.Rent(initialCapacity);
        _index = 0;
    }

    public ArraySegment<byte> WrittenMemory
    {
        get
        {
            Debug.Assert(_rentedBuffer != null);
            Debug.Assert(_index <= _rentedBuffer!.Length);
            return new ArraySegment<byte>(_rentedBuffer, 0, _index);
        }
    }

    private void ClearHelper()
    {
        Debug.Assert(_rentedBuffer != null);
        Debug.Assert(_index <= _rentedBuffer!.Length);

        _rentedBuffer.AsSpan(0, _index).Clear();
        _index = 0;
    }

    // Returns the rented buffer back to the pool
    public void Dispose()
    {
        if (_rentedBuffer == null)
        {
            return;
        }

        ClearHelper();
        byte[] toReturn = _rentedBuffer;
        _rentedBuffer = null;
        ArrayPool<byte>.Shared.Return(toReturn);
    }

    public void Advance(int count)
    {
        Debug.Assert(_rentedBuffer != null);
        Debug.Assert(count >= 0);
        Debug.Assert(_index <= _rentedBuffer!.Length - count);
        _index += count;
    }

    public Memory<byte> GetMemory(int sizeHint = MinimumBufferSize)
    {
        CheckAndResizeBuffer(sizeHint);
        return _rentedBuffer.AsMemory(_index);
    }

    public Span<byte> GetSpan(int sizeHint = MinimumBufferSize)
    {
        CheckAndResizeBuffer(sizeHint);
        return _rentedBuffer.AsSpan(_index);
    }

    private void CheckAndResizeBuffer(int sizeHint)
    {
        Debug.Assert(_rentedBuffer != null);
        Debug.Assert(sizeHint > 0);

        int currentLength = _rentedBuffer!.Length;
        int availableSpace = currentLength - _index;

        // If we've reached ~1GB written, grow to the maximum buffer
        // length to avoid incessant minimal growths causing perf issues.
        if (_index >= MaximumBufferSize / 2)
        {
            sizeHint = Math.Max(sizeHint, MaximumBufferSize - currentLength);
        }

        if (sizeHint > availableSpace)
        {
            int growBy = Math.Max(sizeHint, currentLength);

            int newSize = currentLength + growBy;

            if ((uint)newSize > MaximumBufferSize)
            {
                newSize = currentLength + sizeHint;
                if ((uint)newSize > MaximumBufferSize)
                {
                    ThrowOutOfMemoryException_BufferMaximumSizeExceeded((uint)newSize);
                }
            }

            byte[] oldBuffer = _rentedBuffer;

            _rentedBuffer = ArrayPool<byte>.Shared.Rent(newSize);

            Debug.Assert(oldBuffer.Length >= _index);
            Debug.Assert(_rentedBuffer.Length >= _index);

            Span<byte> oldBufferAsSpan = oldBuffer.AsSpan(0, _index);
            oldBufferAsSpan.CopyTo(_rentedBuffer);
            oldBufferAsSpan.Clear();
            ArrayPool<byte>.Shared.Return(oldBuffer);
        }

        Debug.Assert(_rentedBuffer.Length - _index > 0);
        Debug.Assert(_rentedBuffer.Length - _index >= sizeHint);
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ThrowOutOfMemoryException_BufferMaximumSizeExceeded(uint capacity)
    {
        throw new OutOfMemoryException($"Cannot allocate a buffer of size {capacity}.");
    }
}
