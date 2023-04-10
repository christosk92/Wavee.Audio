using Wavee.Audio.Helpers.Extensions;
using Wavee.Audio.IO;
using Wavee.Audio.Mp3.Frame;

namespace Wavee.Audio.Mp3.Header.InfoTags;

internal static class Vbri
{
    /// <summary>
    /// Try to read a VBRI tag from the provided MPEG frame.
    /// </summary>
    /// <param name="buf"></param>
    /// <param name="header"></param>
    /// <param name="tag"></param>
    /// <returns></returns>
    public static bool TryReadVbriTag(ReadOnlySpan<byte> buf,
        FrameHeader header,
        out VbriTag? tag
    )
    {
        // The VBRI header is a completely optional piece of information. Therefore, flatten an error
        // reading the tag into a None.
        try
        {
            tag = ReadVbriTagInner(buf, header);
            return tag is not null;
        }
        catch (Exception x)
        {
            tag = null;
            return false;
        }
    }

    private static VbriTag? ReadVbriTagInner(ReadOnlySpan<byte> buf, FrameHeader header)
    {
        // Do a quick check that this is a VBRI tag.
        if (!IsMaybeVbriTag(buf, header))
            return null;

        var reader = new BufReader(buf);

        // The VBRI tag is always 32 bytes after the header.
        reader.IgnoreBytes(MpegHeader.MPEG_HEADER_LEN + 32);

        // Check for VBRI header.
        var id = reader.ReadQuadBytes();

        if (!id.SequenceEqual(VBRI_TAG_ID))
            return null;

        // The version is always 1.
        var version = reader.ReadUShortBE();
        if (version != 1)
            return null;

        // Delay is a 2-byte big-endiann floating point value?
        var _delay = reader.ReadUShortBE();
        var _quality = reader.ReadUShortBE();

        var numBytes = reader.ReadUIntBE();
        var numMpegFrames = reader.ReadUIntBE();

        return new VbriTag(
            NumBytes: numBytes,
            NumMpegFrames: numMpegFrames
        );
    }

    private static bool IsMaybeVbriTag(ReadOnlySpan<byte> buf, FrameHeader header)
    {
        const int MIN_VBRI_TAG_LEN = 26;
        const int VBRI_TAG_OFFSET = 32;

        // Only supported with layer 3 packets.
        if (header.Layer is not MpegLayer.Layer3)
            return false;

        // The packet must be big enough to contain a tag.
        if (buf.Length < MIN_VBRI_TAG_LEN + VBRI_TAG_OFFSET)
            return false;

        // The tag ID must be present and correct.
        if (!buf.Slice(VBRI_TAG_OFFSET, 4).SequenceEqual(VBRI_TAG_ID))
            return false;

        // The bytes preceeding the VBRI tag (mostly the side information) should be all 0.
        for (var i = 0; i < VBRI_TAG_OFFSET; i++)
        {
            if (buf[i] != 0)
                return false;
        }

        return true;
    }

    private static byte[] VBRI_TAG_ID = "VBRI"u8.ToArray();
}

internal record VbriTag(uint NumBytes, uint NumMpegFrames);