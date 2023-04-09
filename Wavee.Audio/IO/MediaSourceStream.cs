using Wavee.Audio.Helpers.Extensions;

namespace Wavee.Audio.IO;

/// <summary>
/// <see cref="MediaSourceStream"/> is the main reader type for Wavee.
///
/// By using type erasure and dynamic dispatch, <see cref="MediaSourceStream"/> wraps and hides the inner
/// reader from the consumer, allowing any typical `Read`er to be used with Wavee in a generic
/// way, selectable at runtime.
///
/// <see cref="MediaSourceStream"/> is designed to provide speed and flexibility in a number of challenging I/O
/// scenarios.
///
/// First, to minimize system call and dynamic dispatch overhead on the inner reader, and to
/// amortize that overhead over many bytes, <see cref="MediaSourceStream"/> implements an exponentially growing
/// read-ahead buffer. The read-ahead length starts at 1kB, and doubles in length as more sequential
/// reads are performed until it reaches 32kB. Growing the read-ahead length over time reduces the
/// excess data buffered on consecutive `seek()` calls.
///
/// Second, to better support non-seekable sources, `<see cref="MediaSourceStream"/>` implements a configurable
/// length buffer cache. By default, the buffer caches allows backtracking by up-to the minimum of
/// either `buffer_len - 32kB` or the total number of bytes read since instantiation or the last
/// buffer cache invalidation. Note that regular a `seek()` will invalidate the buffer cache.
/// </summary>
public class MediaSourceStream : IReadBytes, ISeekBuffered, IDisposable
{
    const int MIN_BLOCK_LEN = 1 * 1024;
    const int MAX_BLOCK_LEN = 32 * 1024;

    /// <summary>
    /// The source reader.
    /// </summary>
    private readonly IMediaSource _inner;

    /// <summary>
    /// The ring buffer.
    /// </summary>
    private byte[] _ring;

    /// <summary>
    /// The ring buffer's wrap-around mask.
    /// </summary>
    private int _ringMask;

    /// <summary>
    /// The read position.
    /// </summary>
    private int _readPos;

    /// <summary>
    /// The write position.
    /// </summary>
    private int _writePos;

    /// <summary>
    /// The current block size for a new read.
    /// </summary>
    private int _readBlockLen;

    /// <summary>
    /// Absolute position of the inner stream.
    /// </summary>
    private ulong _absPos;

    /// <summary>
    /// Relative position of the inner stream from the last seek or 0. This is a count of bytes
    /// read from the inner reader since instantiation or the last seek.
    /// </summary>
    private ulong _relPos;

    public MediaSourceStream(Stream source, MediaSourceStreamOptions options)
        : this(new StreamMediaSource(source), options)
    {
    }

    public MediaSourceStream(IMediaSource source, MediaSourceStreamOptions options)
    {
        // The buffer length must be a power of 2, and > the maximum read block length.
        if (options.BufferLength.CountOnes() != 1 ||
            options.BufferLength
            < MAX_BLOCK_LEN)
            throw new ArgumentException("Buffer length must be a power of 2 and >= the maximum read block length.");

        _inner = source;
        _ring = new byte[options.BufferLength];
        _readPos = 0;
        _writePos = 0;
        _readBlockLen = MIN_BLOCK_LEN;
        _absPos = 0;
        _relPos = 0;
    }

    public ulong ReadULong()
    {
        throw new NotImplementedException();
    }

    public uint ReadUInt()
    {
        throw new NotImplementedException();
    }

    public ReadOnlySpan<byte> ReadQuadBytes()
    {
        Span<byte> bytes = new byte[4];

        var buf = ContinguousBuf();

        if (buf.Length >= 4)
        {
            buf[..4].CopyTo(bytes);
            Consume(4);
        }
        else
        {
            foreach (ref var b in bytes)
            {
                b = ReadByte();
            }
        }

        return bytes;
    }

    public byte ReadByte()
    {
        // This function, read_byte, is inlined for performance. To reduce code bloat, place the
        // read-ahead buffer replenishment in a seperate function. Call overhead will be negligible
        // compared to the actual underlying read.
        if (IsBufferExhausted)
        {
            FetchOrEof();
        }

        var value = _ring[_readPos];
        Consume(1);
        return value;
    }

    public int Read(Span<byte> buf)
    {
        int readLen = buf.Length;

        while (!buf.IsEmpty)
        {
            Fetch();

            try
            {
                int count = ReadContiguousBuf(ref buf);

                if (count == 0)
                {
                    break;
                }

                buf = buf.Slice(count);
                Consume(count);
            }
            catch (IOException ex) when (ex.InnerException is OperationCanceledException)
            {
                // Ignore and continue.
            }
        }

        return readLen - buf.Length;
    }

    private int ReadContiguousBuf(ref Span<byte> buf)
    {
        var contiguousBuffer = ContinguousBuf();
        var count = Math.Min(buf.Length, contiguousBuffer.Length);
        contiguousBuffer[..count].CopyTo(buf);
        return count;
    }

    public void ReadExact(Span<byte> buf)
    {
        while (!buf.IsEmpty)
        {
            try
            {
                int count = Read(buf);

                if (count == 0)
                {
                    break;
                }

                buf = buf[count..];
            }
            catch (IOException ex) when (ex.InnerException is OperationCanceledException)
            {
                // Ignore and continue.
            }
        }

        if (!buf.IsEmpty)
        {
            throw new EndOfStreamException();
        }
    }

    public ulong Pos()
    {
        return _absPos - (ulong)UnreadBufferLen();
    }

    public void IgnoreBytes(ulong count)
    {
        throw new NotImplementedException();
    }

    public ulong SeekBuffered(ulong pos)
    {
        var oldPos = Pos();

        // Forward seek.
        int delta = 0;
        if (pos > oldPos)
        {
            //            assert!(pos - old_pos < std::isize::MAX as u64);
            if (pos - oldPos > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(pos));
            }

            delta = (int)(pos - oldPos);
        }
        else if (pos < oldPos)
        {
            // Backward seek.
            if (oldPos - pos > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(pos));
            }

            delta = -(int)(oldPos - pos);
        }
        else
        {
            delta = 0;
        }

        return SeekBufferedRel(delta);
    }

    private ulong SeekBufferedRel(int delta)
    {
        if (delta < 0)
        {
            var absDelta = Math.Min(-delta, ReadBufferLen());
            _readPos = (_readPos + _ring.Length - absDelta) & _ringMask;
        }
        else if (delta > 0)
        {
            var absDelta = Math.Min(delta, UnreadBufferLen());
            _readPos = (_readPos + absDelta) & _ringMask;
        }

        return Pos();
    }

    private int ReadBufferLen()
    {
        if (_writePos >= _readPos)
        {
            return _writePos - _readPos;
        }

        return _writePos + (_ring.Length - _readPos);
    }

    public void EnsureSeekBuffered(int len)
    {
        throw new NotImplementedException();
    }

    public void EnsureSeekbackBuffer(int len)
    {
        int ringLen = _ring.Length;

        int newRingLen = (MAX_BLOCK_LEN + len).NextPowerOfTwo();

        if (ringLen < newRingLen)
        {
            byte[] newRing = new byte[newRingLen];

            int vec0Start = _readPos;
            int vec0End = _writePos >= _readPos ? _writePos : ringLen;
            int vec0Len = vec0End - vec0Start;

            int vec1Start = _writePos < _readPos ? 0 : -1;
            int vec1End = _writePos < _readPos ? _writePos : -1;
            int vec1Len = vec1Start >= 0 ? vec1End - vec1Start : 0;

            Array.Copy(_ring,
                vec0Start, newRing, 0, vec0Len);
            if (vec1Start >= 0)
            {
                Array.Copy(_ring, vec1Start, newRing, vec0Len, vec1Len);
                _writePos = vec0Len + vec1Len;
            }
            else
            {
                _writePos = vec0Len;
            }

            _ring = newRing;
            _ringMask = newRingLen - 1;
            _readPos = 0;
        }
    }

    private ReadOnlySpan<byte> ContinguousBuf()
    {
        if (_writePos >= _readPos)
            return _ring.AsSpan(_readPos, _writePos - _readPos);
        return _ring.AsSpan(_readPos, _ring.Length - _readPos);
    }

    private void Consume(int len)
    {
        _readPos = (_readPos + len) & _ringMask;
    }

    private void FetchOrEof()
    {
        Fetch();

        if (IsBufferExhausted)
        {
            throw new EndOfStreamException();
        }
    }

    private void Fetch()
    {
        if (IsBufferExhausted)
        {
            Span<byte> vec0 = _ring.AsSpan(_writePos);
            int actualReadLen;

            if (vec0.Length >= _readBlockLen)
            {
                actualReadLen = _inner.Read(vec0[.._readBlockLen]);
            }
            else
            {
                int rem = _readBlockLen - vec0.Length;
                Span<byte> vec1 = _ring.AsSpan(0, rem);
                actualReadLen = _inner.Read(vec0);
                actualReadLen += _inner.Read(vec1);
            }

            _writePos = (_writePos + actualReadLen) & (_ring.Length - 1);

            // Update the stream position accounting and read block length here, if needed.
            _absPos += (ulong)actualReadLen;
            _relPos += (ulong)actualReadLen;

            // Grow the read block length exponentially to reduce the overhead of buffering on
            // consecutive seeks.
            _readBlockLen = Math.Min(_readBlockLen << 1, MAX_BLOCK_LEN);
        }
    }

    private int UnreadBufferLen()
    {
        if (_writePos >= _readPos)
            return _writePos - _readPos;

        return _writePos + (_ring.Length - _readPos);
    }

    private bool IsBufferExhausted => _readPos == _writePos;
    public bool CanSeek() => _inner.IsSeekable();

    public void Dispose()
    {
        _inner.Dispose();
    }

    public long? ByteLen() => _inner.ByteLength();

    public ulong Seek(SeekOrigin origin, ulong offset)
    {
        // The current position of the underlying reader is ahead of the current position of the
        // MediaSourceStream by how ever many bytes have not been read from the read-ahead buffer
        // yet. When seeking from the current position adjust the position delta to offset that
        // difference.
        ulong newPosition;

        switch (origin)
        {
            case SeekOrigin.Current:
                var delta = (long)offset - UnreadBufferLen();
                newPosition = (ulong)_inner.Seek(delta, SeekOrigin.Current);
                break;
            default:
                newPosition = offset;
                newPosition = (ulong)_inner.Seek((long)offset, origin);
                break;
        }

        Reset(newPosition);

        return newPosition;
    }

    private void Reset(ulong newPosition)
    {
        _readPos = 0;
        _writePos = 0;
        _readBlockLen = MIN_BLOCK_LEN;
        _absPos = newPosition;
        _relPos = 0;
    }

    public ulong SeekBufferedRev(int delta)
    {
        if (delta >= int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(delta));
        return SeekBufferedRel(-delta);
    }
}

/// <summary>
/// <see cref="MediaSourceStreamOptions"/>` specifies the buffering behaviour of a <see cref="MediaSourceStream"/>.
/// </summary>
/// <param name="BufferLength">The maximum buffer size. Must be a power of 2. Must be > 32kB.</param>
public record MediaSourceStreamOptions(int BufferLength);