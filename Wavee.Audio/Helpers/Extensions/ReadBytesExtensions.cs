using System.Buffers.Binary;
using Wavee.Audio.IO;

namespace Wavee.Audio.Helpers.Extensions;

public static class ReadBytesExtensions
{
    public static uint ReadUIntBE<T>(this T reader) where T : IReadBytes
    {
        return BinaryPrimitives.ReadUInt32BigEndian(reader.ReadQuadBytes());
    }
    public static ushort ReadUShortBE<T>(this T reader) where T : IReadBytes
    {
        return BinaryPrimitives.ReadUInt16BigEndian(reader.ReadDoubleBytes());
    }

    /// <summary>
    /// Reads eight bytes from the stream and interprets them as an unsigned 64-bit little-endian
    /// integer.
    /// </summary>
    /// <returns></returns>
    public static ulong ReadULong<T>(this T reader) where T : IReadBytes
    {
        Span<byte> buf = stackalloc byte[8];
        reader.ReadExact(buf);
        return BinaryPrimitives.ReadUInt64LittleEndian(buf);
    }

    /// <summary>
    /// Reads four bytes from the stream and interprets them as an unsigned 32-bit little-endian
    /// integer.
    /// </summary>
    /// <returns></returns>
    public static uint ReadUInt<T>(this T reader) where T : IReadBytes
    {
        return BinaryPrimitives.ReadUInt32LittleEndian(reader.ReadQuadBytes());
    }
}