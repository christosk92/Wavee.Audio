using Wavee.Audio.Helpers.Extensions;
using Wavee.Audio.IO;
using Wavee.Audio.Mp3.Channel;
using Wavee.Audio.Mp3.Channel.Joint;
using Wavee.Audio.Mp3.Frame;

namespace Wavee.Audio.Mp3.Header;

internal class MpegHeader
{
    /// <summary>
    /// Bit-rate lookup table for MPEG version 1 layer 1.
    /// </summary>
    private static uint[] BIT_RATES_MPEG1_L1 = new uint[]
    {
        0, 32_000, 64_000, 96_000, 128_000, 160_000, 192_000, 224_000, 256_000, 288_000, 320_000,
        352_000, 384_000, 416_000, 448_000
    };

    /// <summary>
    /// Bit-rate lookup table for MPEG version 1 layer 2.
    /// </summary>
    private static uint[] BIT_RATES_MPEG1_L2 = new uint[]
    {
        0, 32_000, 48_000, 56_000, 64_000, 80_000, 96_000, 112_000, 128_000, 160_000, 192_000, 224_000,
        256_000, 320_000, 384_000,
    };

    /// <summary>
    /// Bit-rate lookup table for MPEG version 1 layer 3.
    /// </summary>
    private static uint[] BIT_RATES_MPEG1_L3 = new uint[]
    {
        0, 32_000, 40_000, 48_000, 56_000, 64_000, 80_000, 96_000, 112_000, 128_000, 160_000, 192_000,
        224_000, 256_000, 320_000
    };

    /// <summary>
    /// Bit-rate lookup table for MPEG version 2 & 2.5 audio layer 1.
    /// </summary>
    private static uint[] BIT_RATES_MPEG2_L1 = new uint[]
    {
        0, 32_000, 48_000, 56_000, 64_000, 80_000, 96_000, 112_000, 128_000, 144_000, 160_000, 176_000,
        192_000, 224_000, 256_000
    };

    /// <summary>
    /// Bit-rate lookup table for MPEG version 2 & 2.5 audio layers 2 & 3
    /// </summary>
    private static uint[] BIT_RATES_MPEG2_L23 = new uint[]
    {
        0, 8_000, 16_000, 24_000, 32_000, 40_000, 48_000, 56_000, 64_000, 80_000, 96_000, 112_000,
        128_000, 144_000, 160_000
    };


    public const int MPEG_HEADER_LEN = 4;

    /// <summary>
    /// Basically the point of this code is to parse the mpeg frame header
    /// based on a sync word.
    /// </summary>
    /// <param name="sync"></param>
    /// <returns></returns>
    /// <exception cref="NotImplementedException"></exception>
    internal static FrameHeader ParseFrameHeader(uint header)
    {
        // The MPEG audio header is structured as follows:
        //
        // 0b1111_1111 0b111v_vlly 0brrrr_hhpx 0bmmmm_coee
        // where:
        //     vv   = version, ll = layer      , y = crc
        //     rrrr = bitrate, hh = sample rate, p = padding , x  = private bit
        //     mmmm = mode   , c  = copyright  , o = original, ee = emphasis

        var version = ((header & 0x18_0000) >> 19) switch
        {
            0b00 => MpegVersion.Mpeg2p5,
            0b10 => MpegVersion.Mpeg2,
            0b11 => MpegVersion.Mpeg1,
            _ => throw new NotSupportedException("Invalid MPEG version"),
        };

        var layer = ((header & 0x06_0000) >> 17) switch
        {
            0b01 => MpegLayer.Layer3,
            0b10 => MpegLayer.Layer2,
            0b11 => MpegLayer.Layer1,
            _ => throw new NotSupportedException("Invalid MPEG layer"),
        };

        var br = ((header & 0xf000) >> 12);
        var bitRate = br switch
        {
            // "Free" bit-rate. Note, this is NOT variable bit-rate and is not a mandatory feature of
            // MP3 decoders.
            0b0000 => throw new NotSupportedException("Free bit-rate is not supported"),
            // Invalid bit-rate.
            0b1111 => throw new NotSupportedException("Invalid MPEG bitrate"),
            // MPEG 1 bit-rates.
            _ when version == MpegVersion.Mpeg1 && layer == MpegLayer.Layer1 => BIT_RATES_MPEG1_L1[br],

            _ when version == MpegVersion.Mpeg1 && layer == MpegLayer.Layer2 => BIT_RATES_MPEG1_L2[br],
            _ when version == MpegVersion.Mpeg1 && layer == MpegLayer.Layer3 => BIT_RATES_MPEG1_L3[br],
            _ when layer == MpegLayer.Layer1 => BIT_RATES_MPEG2_L1[br],
            _ => BIT_RATES_MPEG2_L23[br],
        };

        var s = (header & 0xc00) >> 10;
        var (sampleRate, sampleRateIdx) = s switch
        {
            0b00 when version == MpegVersion.Mpeg1 => (44_100, 0),
            0b01 when version == MpegVersion.Mpeg1 => (48_000, 1),
            0b10 when version == MpegVersion.Mpeg1 => (32_000, 2),
            0b00 when version == MpegVersion.Mpeg2 => (22_050, 0),
            0b01 when version == MpegVersion.Mpeg2 => (24_000, 1),
            0b10 when version == MpegVersion.Mpeg2 => (16_000, 2),
            0b00 when version == MpegVersion.Mpeg2p5 => (11_025, 0),
            0b01 when version == MpegVersion.Mpeg2p5 => (12_000, 1),
            0b10 when version == MpegVersion.Mpeg2p5 => (8_000, 2),
            _ => throw new NotSupportedException("Invalid MPEG sample rate"),
        };

        var ch = (header & 0xc0) >> 6;
        var channelMode = ch switch
        {
            0b00 => new StereoChannelMode(),
            0b10 => new DualMonoChannelMode(),
            0b11 => new MonoChannelMode(),
            0b01 when layer == MpegLayer.Layer3 => new JointStereoChannelMode(
                new Layer3JointStereoMode(
                    MidSide: (header & 0x20) != 0,
                    Intensity: (header & 0x10) != 0
                )),
            // Joint stereo mode for layers 1 and 2 only supports Intensity Stereo. The mode extension
            // bits indicate for which sub-bands intensity stereo coding is applied.
            0b01 => new JointStereoChannelMode(
                new IntensityJointStereoMode(
                    Bound: ((header & 0x30) >> 4) << 2)
            ) as IChannelMode
        };

        // Some layer 2 channel and bit-rate combinations are not allowed. Check that the frame does not
        // use them.
        if (layer is MpegLayer.Layer2)
        {
            if (channelMode is MonoChannelMode)
            {
                if (bitRate == 224_00 || bitRate == 256_000 || bitRate == 320_000)
                    throw new NotSupportedException("Invalid bit-rate for mono layer 2 frame");
            }
            else if (bitRate == 32_00 || bitRate == 48_000 || bitRate == 56_000 || bitRate == 80_000)
                throw new NotSupportedException("Invalid bit-rate for stereo layer 2 frame");
        }


        var emphasis = (header & 0x3) switch
        {
            0b00 => Emphasis.None,
            0b01 => Emphasis.Fifty15,
            0b11 => Emphasis.CcitJ17,
            _ => throw new NotSupportedException("Invalid emphasis"),
        };

        var isCopyrighted = (header & 0x8) != 0;
        var isOriginal = (header & 0x4) != 0;
        var hasPadding = (header & 0x2) != 0;

        var hasCrc = (header & 0x10000) == 0;

        // Constants provided for size calculation in section ISO-11172 section 2.4.3.1.
        var factor = layer switch
        {
            MpegLayer.Layer1 => 12,
            MpegLayer.Layer2 => 144,
            MpegLayer.Layer3 when version == MpegVersion.Mpeg1 => 144,
            MpegLayer.Layer3 => 72,
            _ => throw new NotSupportedException("Invalid MPEG layer"),
        };

        // The header specifies the total frame size in "slots". For layers 2 & 3 a slot is 1 byte,
        // however for layer 1 a slot is 4 bytes.
        var slotSize = layer == MpegLayer.Layer1 ? 4 : 1;

        // Calculate the total frame size in number of slots.
        var frameSizeSlots = (factor * (int)bitRate / sampleRate) + (hasPadding ? 1 : 0);

        // Calculate the total frame size in bytes minus the header size.
        var frameSize = (frameSizeSlots * slotSize) - 4;

        return new FrameHeader(
            Version: version,
            Layer: layer,
            Bitrate: bitRate,
            SampleRate: (uint)sampleRate,
            SampleRateIdx: sampleRateIdx,
            ChannelMode: channelMode,
            Emphasis: emphasis,
            IsOriginal: isOriginal,
            IsCopyrighted: isCopyrighted,
            HasPadding: hasPadding,
            HasCrc: hasCrc,
            FrameSize: frameSize
        );
    }

    public static bool IsSyncWord(uint sync)
    {
        return (sync & 0xfff0_0000) == 0xfff0_0000;
    }

    public static uint SyncFrame<T>(T reader) where T : IReadBytes
    {
        uint sync = 0;

        while (true)
        {
            // Synchronize stream to the next frame using the sync word. The MPEG audio frame header
            // always starts at a byte boundary with 0xffe (11 consecutive 1 bits.) if supporting up to
            // MPEG version 2.5.
            while (!IsSyncWord(sync))
            {
                var r = (uint)reader.ReadByte();
                sync = (sync << 8) | r;
            }

            // Random data can look like a sync word. Do a quick check to increase confidence that
            // this is may be the start of a frame.
            if (CheckHeader(sync))
                break;

            var r2 = (uint)reader.ReadByte();
            sync = (sync << 8) | r2;
        }

        return sync;
    }

    /// <summary>
    /// Quickly check if a header sync word may be valid.
    /// </summary>
    /// <param name="sync"></param>
    /// <returns></returns>
    /// <exception cref="NotImplementedException"></exception>
    private static bool CheckHeader(uint header)
    {
        //Version (0x1 is not allowed)
        // Layer (0x0 is not allowed).
        // BItrate (0xf is not allowed)
        // Sampling rate (0x3 is not allowed)
        //Emphasis (0x2 is not allowed)

        if (((header >> 19) & 0x3) == 0x1) return false;
        if (((header >> 17) & 0x3) == 0x0) return false;
        if (((header >> 12) & 0xf) == 0xf) return false;
        if (((header >> 10) & 0x3) == 0x3) return false;
        if ((header & 0x3) == 0x2) return false;

        return true;
    }


    /// <summary>
    /// Synchronize the stream to the start of the next MPEG audio frame header, then read and return
    /// the frame header or an error.
    /// </summary>
    /// <param name="reader"></param>
    /// <returns></returns>
    /// <exception cref="NotImplementedException"></exception>
    public static FrameHeader ReadFrameHeader<T>(T reader) where T : IReadBytes
    {
        // Synchronize and parse the frame header.
        var sync = SyncFrame(reader);
        return ParseFrameHeader(sync);
    }

    public static uint ReadFrameHeaderWordNoSync(MediaSourceStream reader)
    {
        return reader.ReadUIntBE();
    }
}

internal enum Emphasis
{
    None,
    Fifty15,
    CcitJ17
}