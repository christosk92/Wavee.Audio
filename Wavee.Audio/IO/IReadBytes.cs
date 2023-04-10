using System.Buffers.Binary;

namespace Wavee.Audio.IO;

public interface IReadBytes
{
    /// <summary>
    /// Reads four bytes from the stream and returns them in read-order or an error.
    /// </summary>
    /// <returns></returns>
    ReadOnlySpan<byte> ReadQuadBytes();

    ReadOnlySpan<byte> ReadDoubleBytes();

    ReadOnlySpan<byte> ReadTripleBytes();

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

    /// <summary>
    /// Reads three bytes from the stream and interprets them as an unsigned 24-bit big-endian
    /// integer or returns an error.
    /// </summary>
    /// <returns></returns>
    /// <exception cref="NotImplementedException"></exception>
    uint ReadBeU24()
    {
        Span<byte> buf = stackalloc byte[sizeof(uint)];
        ReadTripleBytes().CopyTo(buf[0..3]);
        return BinaryPrimitives.ReadUInt32BigEndian(buf) >> 8;
    }
}