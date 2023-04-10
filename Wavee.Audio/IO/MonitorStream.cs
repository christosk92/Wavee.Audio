using Wavee.Audio.Checksum;

namespace Wavee.Audio.IO;

/// <summary>
/// A <see cref="MonitorStream"/> is a passive stream that observes all operations performed on the inner
/// stream and forwards an immutable reference of the result to a [`Monitor`].
/// </summary>
public sealed class MonitorStream<B, M> : IReadBytes where B : IReadBytes where M : IMonitor
{
    private readonly B _inner;
    private readonly M _monitor;

    public MonitorStream(B inner, M monitor)
    {
        _inner = inner;
        _monitor = monitor;
    }

    public M Monitor => _monitor;
    public B Inner => _inner;

    public ReadOnlySpan<byte> ReadQuadBytes()
    {
        var bytes = _inner.ReadQuadBytes();
        _monitor.ProcessQuadBytes(bytes);
        return bytes;
    }

    public ReadOnlySpan<byte> ReadDoubleBytes()
    {
        var bytes = _inner.ReadDoubleBytes();
        _monitor.ProcessDoubleBytes(bytes);
        return bytes;
    }

    public ReadOnlySpan<byte> ReadTripleBytes()
    {
        var bytes = _inner.ReadTripleBytes();
        _monitor.ProcessTripleBytes(bytes);
        return bytes;
    }

    public byte ReadByte()
    {
        var b = _inner.ReadByte();
        _monitor.ProcessByte(b);
        return b;
    }

    public void ReadExact(Span<byte> buf)
    {
        _inner.ReadExact(buf);
        _monitor.ProcessBufBytes(buf);
    }

    public ulong Pos()
    {
        throw new NotImplementedException();
    }

    public void IgnoreBytes(ulong count)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// A <see cref="IMonitor"/> provides a common interface to examine the operations observed be
/// a <see cref="MonitorStream{B,M}"/>
/// </summary>
public interface IMonitor
{
    void ProcessByte(byte b);
    void ProcessBufBytes(ReadOnlySpan<byte> buf);

    void ProcessQuadBytes(ReadOnlySpan<byte> buf)
    {
        ProcessByte(buf[0]);
        ProcessByte(buf[1]);
        ProcessByte(buf[2]);
        ProcessByte(buf[3]);
    }

    void ProcessDoubleBytes(ReadOnlySpan<byte> buf)
    {
        ProcessByte(buf[0]);
        ProcessByte(buf[1]);
    }

    void ProcessTripleBytes(ReadOnlySpan<byte> buf)
    {
        ProcessByte(buf[0]);
        ProcessByte(buf[1]);
        ProcessByte(buf[2]);
    }
}