namespace Wavee.Audio.IO;

/// <summary>
/// A <see cref="ScopedStream"/> restricts the number of bytes that may be read to an upper limit.
/// </summary>
public sealed class ScopedStream<T> : IReadBytes, ISeekBuffered where T : IReadBytes
{
    private T _inner;
    private ulong _start;
    private ulong _len;
    private ulong _read;

    public ScopedStream(T inner, ulong len)
    {
        _inner = inner;
        _start = inner.Pos();
        _len = len;
        _read = 0;
    }

    public T Inner => _inner;

    public ReadOnlySpan<byte> ReadQuadBytes()
    {
        if (_len - _read < 4)
            throw new ArgumentOutOfRangeException();

        _read += 4;
        return _inner.ReadQuadBytes();
    }

    public byte ReadByte()
    {
        if (_len - _read < 1)
            throw new ArgumentOutOfRangeException();
        
        _read += 1;
        return _inner.ReadByte();
    }

    public void ReadExact(Span<byte> buf)
    {
        if (_len - _read < (ulong)buf.Length)
            throw new ArgumentOutOfRangeException();

        _read += (ulong)buf.Length;
        _inner.ReadExact(buf);
    }

    public ulong Pos() => _inner.Pos();

    public void IgnoreBytes(ulong count)
    {
        throw new NotImplementedException();
    }

    public ulong SeekBuffered(ulong pos)
    {
        throw new NotImplementedException();
    }

    public void EnsureSeekBuffered(int len)
    {
        throw new NotImplementedException();
    }
}