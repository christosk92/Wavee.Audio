using Wavee.Audio.Helpers.Extensions;
using Wavee.Audio.IO;
using Wavee.Audio.Vorbis.Exception;

namespace Wavee.Audio.Vorbis.Pages;

internal class OggPageHeader
{
    internal static readonly byte[] OGG_PAGE_MARKER = "OggS"u8.ToArray();
    internal const int OGG_PAGE_MAX_SIZE = OGG_PAGE_HEADER_SIZE + 255 + 255 * 255;
    internal const int OGG_PAGE_HEADER_SIZE = 27;

    public OggPageHeader(byte version,
        ulong absgp,
        uint serial,
        uint sequence,
        uint crc,
        byte nSegments,
        bool isContinuation,
        bool isFirstPage,
        bool isLastPage)
    {
        Version = version;
        Serial = serial;
        Sequence = sequence;
        Crc = crc;
        NSegments = nSegments;
        IsContinuation = isContinuation;
        IsFirstPage = isFirstPage;
        IsLastPage = isLastPage;
        AbsGp = absgp;
    }

    public byte Version { get; }
    public ulong AbsGp { get; }
    public uint Serial { get; }
    public uint Sequence { get; }
    public uint Crc { get; }
    public byte NSegments { get; }
    public bool IsContinuation { get; }
    public bool IsLastPage { get; }
    public bool IsFirstPage { get; }

    public static OggPageHeader ReadPageHeader<T>(T reader) where T : IReadBytes
    {
        // The OggS marker should be present.
        var marker = reader.ReadQuadBytes();

        if (!marker.SequenceEqual(OGG_PAGE_MARKER))
        {
            throw new OggDecodeException(DecodeErrorType.MissingOggPageMarker);
        }

        var version = reader.ReadByte();
        // There is only one OGG version, and that is version 0.
        if (version != 0)
        {
            throw new OggDecodeException(DecodeErrorType.InvalidOggPageVersion);
        }

        var flags = reader.ReadByte();

        // Only the first 3 least-significant bits are used for flags.
        if ((flags & 0xf8) != 0)
        {
            throw new OggDecodeException(DecodeErrorType.InvalidOggPageFlags);
        }

        var ts = reader.ReadULong();
        var serial = reader.ReadUInt();
        var seq = reader.ReadUInt();
        var crc = reader.ReadUInt();
        var segs = reader.ReadByte();

        return new OggPageHeader(
            version: version,
            absgp: ts,
            serial: serial,
            sequence: seq,
            crc: crc,
            nSegments: segs,
            isContinuation: (flags & 0x01) != 0,
            isFirstPage: (flags & 0x02) != 0,
            isLastPage: (flags & 0x04) != 0
        );
    }
}