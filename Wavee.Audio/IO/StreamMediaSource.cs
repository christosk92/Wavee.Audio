namespace Wavee.Audio.IO;

internal sealed class StreamMediaSource : IMediaSource
{
    private readonly Stream _stream;

    public StreamMediaSource(Stream source)
    {
        _stream = source;
    }

    public int Read(Span<byte> buffer)
    {
        return _stream.Read(buffer);
    }

    public long Seek(long offset, SeekOrigin origin)
    {
        return _stream.Seek(offset, origin);
    }

    public bool IsSeekable() => _stream.CanSeek;

    public long? ByteLength() => _stream.CanSeek ? _stream.Length : null;

    public void Dispose()
    {
        _stream.Dispose();
    }
}