namespace Wavee.Audio.IO;

/// <summary>
/// <see cref="IMediaSource"/> is a composite interface that represents the required functionality of
/// <see cref="Stream"/>. A source *must* implement this interface to be used by <see cref="MediaSourceStream"/>.
/// Despite requiring the seek capability, seeking is an optional capability that can be
/// queried at runtime.
/// </summary>
public interface IMediaSource : IDisposable
{
    /// <summary>
    /// Reads up to `buffer.Length` bytes into the given buffer. Returns the number of bytes read.
    /// </summary>
    /// <param name="buffer"></param>
    /// <returns></returns>
    int Read(Span<byte> buffer);
    
    /// <summary>
    /// Seeks to the given offset from the given origin. The offset is relative to the origin.
    /// </summary>
    /// <param name="offset"></param>
    /// <param name="origin"></param>
    /// <returns></returns>
    long Seek(long offset, SeekOrigin origin);

    /// <summary>
    /// Returns if the source is seekable. This may be an expensive operation.
    /// </summary>
    bool IsSeekable();
    
    /// <summary>
    /// Returns the length in bytes, if available. This may be an expensive operation.
    /// </summary>
    /// <returns></returns>
    long? ByteLength();
}