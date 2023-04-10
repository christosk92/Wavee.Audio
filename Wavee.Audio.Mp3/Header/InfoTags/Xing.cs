using System.Diagnostics;
using System.Text;
using Wavee.Audio.Helpers.Extensions;
using Wavee.Audio.IO;
using Wavee.Audio.Mp3.Crc;
using Wavee.Audio.Mp3.Frame;

namespace Wavee.Audio.Mp3.Header.InfoTags;

internal static class Xing
{
    /// <summary>
    /// Try to read a Xing/Info tag from the provided MPEG frame.
    /// </summary>
    /// <param name="packet"></param>
    /// <param name="header"></param>
    /// <param name="o"></param>
    /// <returns></returns>
    /// <exception cref="NotImplementedException"></exception>
    public static bool TryReadInfoTag(ReadOnlySpan<byte> buf, FrameHeader header, out XingInfoTag? o)
    {
        // The Info header is a completely optional piece of information. Therefore, flatten an error
        // reading the tag into a None.
        try
        {
            o = TryReadInfoTagInner(buf, header);
            return o != null;
        }
        catch (Exception x)
        {
            o = null;
            return false;
        }
    }

    private static XingInfoTag? TryReadInfoTagInner(ReadOnlySpan<byte> buf, FrameHeader header)
    {
        // Do a quick check that this is a Xing/Info tag.
        if (!IsMaybeInfoTag(buf, header))
            return null;

        // The position of the Xing/Info tag relative to the end of the header. This is equal to the
        // side information length for the frame.
        var offset = header.SideInfoLength();

        // Start the CRC with the header and side information.
        var crc16 = new Crc16AnsiLe(0);
        crc16.ProcessBufBytes(buf[..(offset + MpegHeader.MPEG_HEADER_LEN)]);

        // Start reading the Xing/Info tag after the side information.
        var reader =
            new MonitorStream<BufReader, Crc16AnsiLe>(new BufReader(buf[(offset + MpegHeader.MPEG_HEADER_LEN)..]),
                crc16);

        // Check for Xing/Info header.
        var id = reader.ReadQuadBytes();

        if (!id.SequenceEqual(XING_TAG_ID) && !id.SequenceEqual(INFO_TAG_ID))
            return null;

        // The "Info" id is used for CBR files.
        var isCbr = id.SequenceEqual(INFO_TAG_ID);

        // Flags indicates what information is provided in this Xing/Info tag.
        var flags = reader.ReadUIntBE();

        var numFrames = (flags & 0x1) != 0 ? reader.ReadUIntBE() : (uint?)null;
        var numBytes = (flags & 0x2) != 0 ? reader.ReadUIntBE() : (uint?)null;
        Memory<byte>? toc = null;
        if ((flags & 0x4) != 0)
        {
            toc = new byte[100];
            reader.ReadExact(toc.Value.Span);
        }

        var quality = (flags & 0x8) != 0 ? reader.ReadUIntBE() : (uint?)null;

        const ulong LAME_EXT_LEN = 36;
        const ulong MIN_LAME_EXT_LEN = 24;


        // The LAME extension may not always be present, or complete. The important fields in the
        // extension are within the first 24 bytes. Therefore, try to read those if they're available.
        LameTag? lame = null;
        if (reader.Inner.BytesAvailable() >= MIN_LAME_EXT_LEN)
        {
            Span<byte> encoder = stackalloc byte[9];
            reader.ReadExact(encoder);

            //Revision
            var _revinfo = reader.ReadByte();

            //Lowpass filter value
            var _lowpass = reader.ReadByte();

            // Replay gain peak in 9.23 (bit) fixed-point format.
            var replaygainpeak = reader.ReadUIntBE() switch
            {
                0 => (float?)null,
                var x => (32767.0f * (x / (float)Math.Pow(2, 23)))
            };

            // Radio replay gain.
            var replaygainRadio = ParseLameTagReplayGain(reader.ReadUShortBE(), 1);

            // Audiophile replay gain.
            var replaygainAudiophile = ParseLameTagReplayGain(reader.ReadUShortBE(), 2);

            // Encoding flags & ATH type.
            var _encodingFlags = reader.ReadByte();

            // Bitrate.
            var _abr = reader.ReadByte();

            (uint encDelay, uint encPadding) = (0, 0);
            var trim = (reader as IReadBytes).ReadBeU24();
            if (encoder[..4].SequenceEqual("LAME"u8) ||
                encoder[..4].SequenceEqual("Lavf"u8)
                || encoder[..4].SequenceEqual("Lavc"u8))
            {
                var delay = 528 + 1 + (trim >> 12);
                var padding = trim & ((1 << 12) - 1);

                encDelay = delay;
                encPadding = (uint)Math.Max((int)padding - (528u + 1u), 0u);
            }

            // If possible, attempt to read the extra fields of the extension if they weren't
            // truncated.
            ushort? crc = null;
            if (reader.Inner.BytesAvailable() >= LAME_EXT_LEN - MIN_LAME_EXT_LEN)
            {
                // Flags.
                var _misc = reader.ReadByte();

                // mp3 gain.
                var _mp3Gain = reader.ReadByte();

                //Preset & surround info.
                var _surrInfo = reader.ReadUShortBE();

                // Music length.
                var _musicLength = reader.ReadUIntBE();

                // Music CRC.
                var _musicRc = reader.ReadUShortBE();


                // The tag CRC. LAME always includes this CRC regardless of the protection bit, but
                // other encoders may only do so if the protection bit is set.
                if (header.HasCrc || encoder[..4].SequenceEqual("LAME"u8))
                    crc = reader.Inner.ReadUShortBE();
            }

            // If there is no CRC, then assume the tag is correct. Otherwise, use the CRC.
            var isTagOk = crc == null || crc == reader.Monitor.Crc;

            if (isTagOk)
            {
                // The CRC matched or is not present.
                lame = new LameTag(
                    Encoder: Encoding.UTF8.GetString(encoder),
                    ReplayGainPeak: replaygainpeak,
                    ReplayGainRadio: replaygainRadio,
                    ReplayGainAudiophile: replaygainAudiophile,
                    EncDelay: encDelay,
                    EncPadding: encPadding);
            }
            else
            {
                // The CRC did not match, this is probably not a LAME tag.
                Debug.WriteLine("LAME tag CRC mismatch");
                lame = null;
            }
        }
        else
        {
            // Frame not large enough for a LAME tag.
            Debug.WriteLine("Frame not large enough for LAME tag");
            lame = null;
        }

        return new XingInfoTag(
            NumFrames: numFrames,
            NumBytes: numBytes,
            Toc: toc,
            Quality: quality,
            IsCbr: isCbr,
            Lame: lame
        );
    }


    private static float? ParseLameTagReplayGain(ushort value, byte expectedName)
    {
        // The 3 most-significant bits are the name code.
        var name = (byte)((value & 0xE000) >> 13);

        if (name != expectedName)
            return null;

        var num = (float)(value & 0x1FFF);
        var gain = num / (float)10.0;
        if ((value & 0x200) != 0)
            return -gain;
        return gain;
    }

    static byte[] XING_TAG_ID =
        "Xing"u8.ToArray();

    static byte[] INFO_TAG_ID =
        "Info"u8.ToArray();

    private static bool IsMaybeInfoTag(ReadOnlySpan<byte> buf, FrameHeader header)
    {
        const int MIN_XING_TAG_SIZE = 4;

        // Only supported with layer 3 packets.
        if (header.Layer != MpegLayer.Layer3)
            return false;

        // The position of the Xing/Info tag relative to the start of the packet. This is equal to the
        // side information length for the frame.
        var offset = header.SideInfoLength() + MpegHeader.MPEG_HEADER_LEN;

        if (buf.Length < offset + MIN_XING_TAG_SIZE)
            return false;

        var id = buf.Slice(offset, 4);

        if (!id.SequenceEqual(XING_TAG_ID) && !id.SequenceEqual(INFO_TAG_ID))
            return false;

        for (int i = MpegHeader.MPEG_HEADER_LEN; i < offset; i++)
        {
            if (buf[i] != 0)
                return false;
        }

        return true;
    }
}

internal record LameTag(
    string Encoder,
    float? ReplayGainPeak,
    float? ReplayGainRadio,
    float? ReplayGainAudiophile,
    uint EncDelay,
    uint EncPadding
);

internal record XingInfoTag(
    uint? NumFrames,
    uint? NumBytes,
    Memory<byte>? Toc,
    uint? Quality,
    bool IsCbr,
    LameTag? Lame
);