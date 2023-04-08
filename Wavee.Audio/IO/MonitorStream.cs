using Wavee.Audio.Checksum;

namespace Wavee.Audio.IO;

/// <summary>
/// A <see cref="MonitorStream"/> is a passive stream that observes all operations performed on the inner
/// stream and forwards an immutable reference of the result to a [`Monitor`].
/// </summary>
public sealed class MonitorStream<B, M> : IReadBytes where B : IReadBytes, ISeekBuffered where M : IMonitor
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
        throw new NotImplementedException();
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
}