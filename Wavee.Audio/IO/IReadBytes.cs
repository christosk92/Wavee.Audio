namespace Wavee.Audio.IO;

public interface IReadBytes
{
    /// <summary>
    /// Reads four bytes from the stream and returns them in read-order or an error.
    /// </summary>
    /// <returns></returns>
    ReadOnlySpan<byte> ReadQuadBytes();

    /// <summary>
    /// Reads a single byte from the stream and returns it or an error.
    /// </summary>
    /// <returns></returns>
    byte ReadByte();

    /// <summary>
    /// Reads exactly the number of bytes required to fill be provided buffer or returns an error.
    /// </summary>
    /// <param name="buf">The buffer to read into.</param>
    void ReadExact(Span<byte> buf);

    /// <summary>
    /// Gets the position of the stream.
    /// </summary>
    /// <returns>The position of the stream in bytes.</returns>
    ulong Pos();

    void IgnoreBytes(ulong count);
}