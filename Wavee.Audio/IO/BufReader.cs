namespace Wavee.Audio.IO;

/// <summary>
/// A <see cref="BufReader"/> reads bytes from a byte buffer.
/// </summary>
public sealed class BufReader : IReadBytes
{
    private byte[] _buf;
    private int _pos;

    public BufReader(ReadOnlySpan<byte> headerBuf)
    {
        _buf = headerBuf.ToArray();
        _pos = 0;
    }

    public ReadOnlySpan<byte> ReadQuadBytes()
    {
        if (_buf.Length - _pos < 4)
            throw new InternalBufferOverflowException("Not enough bytes in buffer");

        Span<byte> bytes = new byte[4];
        _buf.AsSpan(_pos, 4).CopyTo(bytes);
        _pos += 4;
        return bytes;
    }

    public ReadOnlySpan<byte> ReadDoubleBytes()
    {
        if (_buf.Length - _pos < 2)
            throw new InternalBufferOverflowException("Not enough bytes in buffer");

        Span<byte> bytes = new byte[2];
        _buf.AsSpan(_pos, 2).CopyTo(bytes);
        _pos += 2;
        return bytes;
    }

    public ReadOnlySpan<byte> ReadTripleBytes()
    {
        if (_buf.Length - _pos < 3)
            throw new InternalBufferOverflowException("Not enough bytes in buffer");

        Span<byte> bytes = new byte[3];
        _buf.AsSpan(_pos, 3).CopyTo(bytes);
        _pos += 3;
        return bytes;
    }

    public byte ReadByte()
    {
        if (_buf.Length - _pos < 1)
            throw new InternalBufferOverflowException("Not enough bytes in buffer");

        _pos += 1;
        return _buf[_pos - 1];
    }

    public void ReadExact(Span<byte> buf)
    {
        var len = buf.Length;

        if (_buf.Length - _pos < len)
            throw new InternalBufferOverflowException("Not enough bytes in buffer");

        _buf.AsSpan(_pos, len).CopyTo(buf);
        _pos += len;
    }

    public ulong Pos() => (ulong)_pos;

    public void IgnoreBytes(ulong count)
    {
        if (_buf.Length - _pos < (int)count)
            throw new InternalBufferOverflowException("Not enough bytes in buffer");

        _pos += (int)count;
    }

    public Memory<byte> ReadBufBytesAvailable()
    {
        var pos = _pos;
        _pos = _buf.Length;
        return _buf.AsMemory(pos);
    }

    public ulong BytesAvailable()
    {
        return (ulong)(_buf.Length - _pos);
    }
}