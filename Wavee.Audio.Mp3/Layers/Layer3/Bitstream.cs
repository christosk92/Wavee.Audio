using Wavee.Audio.IO;
using Wavee.Audio.Mp3.Channel;
using Wavee.Audio.Mp3.Frame;
using Wavee.Audio.Mp3.Header;

namespace Wavee.Audio.Mp3.Layers.Layer3;

internal sealed class Bitstream
{
    /// <summary>
    /// Pairs of bit lengths for MPEG version 1 scale factors. For MPEG version 1, there are two
    /// possible bit lengths for scale factors: slen1 and slen2. The first N of bands have scale factors
    /// of bit length slen1, while the remaining bands have length slen2. The value of the switch point,
    /// N, is determined by block type.
    ///
    /// This table is indexed by scalefac_compress.
    /// </summary>
    static (uint, uint)[] SCALE_FACTOR_SLEN = new (uint, uint)[]
    {
        (0, 0), (0, 1), (0, 2), (0, 3), (3, 0), (1, 1), (1, 2), (1, 3),
        (2, 1), (2, 2), (2, 3), (3, 1), (3, 2), (3, 3), (4, 2), (4, 3)
    };

    private static readonly int[][][] ScaleFactorMpeg2Nsfb = new int[][][]
    {
        // Intensity stereo channel modes.
        new int[][] { new int[] { 7, 7, 7, 0 }, new int[] { 12, 12, 12, 0 }, new int[] { 6, 15, 12, 0 } },
        new int[][] { new int[] { 6, 6, 6, 3 }, new int[] { 12, 9, 9, 6 }, new int[] { 6, 12, 9, 6 } },
        new int[][] { new int[] { 8, 8, 5, 0 }, new int[] { 15, 12, 9, 0 }, new int[] { 6, 18, 9, 0 } },
        // Other channel modes.
        new int[][] { new int[] { 6, 5, 5, 5 }, new int[] { 9, 9, 9, 9 }, new int[] { 6, 9, 9, 9 } },
        new int[][] { new int[] { 6, 5, 7, 3 }, new int[] { 9, 9, 12, 6 }, new int[] { 6, 9, 12, 6 } },
        new int[][] { new int[] { 11, 10, 0, 0 }, new int[] { 18, 18, 0, 0 }, new int[] { 15, 18, 0, 0 } }
    };

    private static int[][] SFB_LONG_BANDS = new int[][]
    {
        // 44.1 kHz, MPEG version 1, derived from ISO/IEC 11172-3 Table B.8
        new int[]
        {
            0, 4, 8, 12, 16, 20, 24, 30, 36, 44, 52, 62, 74, 90, 110, 134, 162, 196, 238, 288, 342,
            418, 576
        },
        // 48 kHz
        new int[]
        {
            0, 4, 8, 12, 16, 20, 24, 30, 36, 42, 50, 60, 72, 88, 106, 128, 156, 190, 230, 276, 330,
            384, 576,
        },
        // 32 kHz
        new int[]
        {
            0, 4, 8, 12, 16, 20, 24, 30, 36, 44, 54, 66, 82, 102, 126, 156, 194, 240, 296, 364, 448,
            550, 576,
        },
        // 22.050 kHz, MPEG version 2, derived from ISO/IEC 13818-3 Table B.2
        new int[]
        {
            0, 6, 12, 18, 24, 30, 36, 44, 54, 66, 80, 96, 116, 140, 168, 200, 238, 284, 336, 396, 464,
            522, 576,
        },
        // 24 kHz (the band starting at 332 starts at 330 in some decoders, but 332 is correct)
        new int[]
        {
            0, 6, 12, 18, 24, 30, 36, 44, 54, 66, 80, 96, 114, 136, 162, 194, 232, 278, 332, 394, 464,
            540, 576,
        },
        // 16 kHz
        new int[]
        {
            0, 6, 12, 18, 24, 30, 36, 44, 54, 66, 80, 96, 116, 140, 168, 200, 238, 284, 336, 396, 464,
            522, 576,
        },
        // 11.025 kHz, MPEG version 2.5
        new int[]
        {
            0, 6, 12, 18, 24, 30, 36, 44, 54, 66, 80, 96, 116, 140, 168, 200, 238, 284, 336, 396, 464,
            522, 576,
        },
        // 12 kHz
        new[]
        {
            0, 6, 12, 18, 24, 30, 36, 44, 54, 66, 80, 96, 116, 140, 168, 200, 238, 284, 336, 396, 464,
            522, 576,
        },
        // 8 kHz
        new[]
        {
            0, 12, 24, 36, 48, 60, 72, 88, 108, 132, 160, 192, 232, 280, 336, 400, 476, 566, 568, 570,
            572, 574, 576,
        }
    };

    public static int ReadSideInfo(BitReaderLtr bs, FrameHeader header, FrameData frameDate)
    {
        // For MPEG version 1...
        if (header.Version is MpegVersion.Mpeg1)
        {
            // First 9 bits is main_data_begin.
            frameDate.MainDataBegin = (ushort)bs.ReadBitsLeq32(9);

            // Next 3 (>1 channel) or 5 (1 channel) bits are private and should be ignored.
            switch (header.ChannelMode)
            {
                case MonoChannelMode:
                    bs.IgnoreBits(5);
                    break;
                default:
                    bs.IgnoreBits(3);
                    break;
            }

            // Next four (or 8, if more than one channel) are the SCFSI bits.
            foreach (var scfsi in frameDate.ScfsiMut(header.NumberOfChannels()))
            {
                for (int i = 0; i < 4; i++)
                {
                    scfsi[i] = bs.ReadBool();
                }
            }
        }
        else
        {
            // For MPEG version 2...
            // First 8 bits is main_data_begin.
            frameDate.MainDataBegin = (ushort)bs.ReadBitsLeq32(8);

            // Next 1 (1 channel) or 2 (>1 channel) bits are private and should be ignored.
            switch (header.ChannelMode)
            {
                case MonoChannelMode:
                    bs.IgnoreBits(1);
                    break;
                default:
                    bs.IgnoreBits(2);
                    break;
            }
        }

        // Read the side_info for each granule.
        foreach (var granule in frameDate.GranulesMut(header.Version))
        {
            ReadGranuleSideInfo(bs, granule, header);
        }

        return header.SideInfoLength();
    }

    private static void ReadGranuleSideInfo(BitReaderLtr bs,
        Granule granule,
        FrameHeader header)
    {
        // Read the side_info for each channel in the granule.
        foreach (var channel in granule.Channels.Span[..(header.ChannelMode is MonoChannelMode ? 1 : 2)])
        {
            ReadGranuleChannelSideInfo(bs, channel, header);
        }
    }

    /// <summary>
    /// Reads the side_info for a single channel in a granule from a `BitStream`.
    /// </summary>
    /// <param name="bs"></param>
    /// <param name="channel"></param>
    /// <param name="header"></param>
    /// <exception cref="NotImplementedException"></exception>
    private static void ReadGranuleChannelSideInfo(BitReaderLtr bs,
        GranuleChannel channel,
        FrameHeader header)
    {
        channel.Part2_3Length = (ushort)bs.ReadBitsLeq32(12);
        channel.BigValues = (ushort)bs.ReadBitsLeq32(9);

        // The maximum number of samples in a granule is 576. One big_value decodes to 2 samples,
        // therefore there can be no more than 288 (576/2) big_values.
        if (channel.BigValues > 288)
        {
            throw new NotImplementedException();
        }

        channel.GlobalGain = (byte)bs.ReadBitsLeq32(8);

        channel.ScalefacCompress =
            (ushort)(header.Version is MpegVersion.Mpeg1 ? bs.ReadBitsLeq32(4) : bs.ReadBitsLeq32(9));

        var windowSwitching = bs.ReadBool();

        if (windowSwitching)
        {
            var blockTypeEnc = bs.ReadBitsLeq32(2);
            var isMixed = bs.ReadBool();

            channel.BlockType = blockTypeEnc switch
            {
                0b00 => throw new NotSupportedException("Block type 0 is reserved."),
                0b01 => BlockType.Start,
                0b10 when isMixed => BlockType.ShortMixed,
                0b10 => BlockType.Short,
                0b11 => BlockType.End,
                _ => throw new NotSupportedException("Block type 3 is reserved.")
            };

            // When window switching is used, there are only two regions, therefore there are only
            // two table selectors.
            for (var i = 0; i < 2; i++)
            {
                channel.TableSelect[i] = (byte)bs.ReadBitsLeq32(5);
            }

            for (var i = 0; i < 3; i++)
            {
                channel.SubBlockGain[i] = (byte)bs.ReadBitsLeq32(3);
            }

            // When using window switching, the boundaries of region[0..3] are set implicitly according
            // to the MPEG version and block type. Below, the boundaries to set as per the applicable
            // standard.
            //
            // If MPEG version 2.5 specifically...
            if (header.Version is MpegVersion.Mpeg2p5)
            {
                // For MPEG2.5, the number of scale-factor bands in region0 depends on the block type.
                // The standard indicates these values as 1 less than the actual value, therefore 1 is
                // added here to both values.
                var region0Count = channel.BlockType switch
                {
                    BlockType.Short => (5 + 1),
                    _ => (7 + 1)
                };

                channel.Region1Start = SFB_LONG_BANDS[header.SampleRateIdx][region0Count];
            }
            // If MPEG version 1, OR the block type is Short...
            else if (header.Version is MpegVersion.Mpeg1 || blockTypeEnc == 0b10)
            {
                // For MPEG1 with transitional LONG blocks, the first 8 LONG scale-factor bands are used
                // for region0. These bands are always [4, 4, 4, 4, 4, 4, 6, 6, ...] regardless of
                // sample rate. These bands sum to 36 samples.
                //
                // For MPEG1 with SHORT blocks, the first 9 SHORT scale-factor bands are used for
                // region0. These band are always [4, 4, 4, 4, 4, 4, 4, 4, 4, ...] regardless of sample
                // rate. These bands also sum to 36 samples.
                //
                // Finally, for MPEG2 with SHORT blocks, the first 9 short scale-factor bands are used
                // for region0. These bands are also always  [4, 4, 4, 4, 4, 4, 4, 4, 4, ...] regardless
                // of sample and thus sum to 36 samples.
                //
                // In all cases, the region0_count is 36.
                //
                // TODO: This is not accurate for MPEG2.5 at 8kHz.
                channel.Region1Start = 36;
            }
            // If MPEG version 2 AND the block type is not Short...
            else
            {
                // For MPEG2 and transitional LONG blocks, the first 8 LONG scale-factor bands are used
                // for region0. These bands are always [6, 6, 6, 6, 6, 6, 8, 10, ...] regardless of
                // sample rate. These bands sum to 54.
                channel.Region1Start = 54;
            }

            // The second region, region1, spans the remaining samples. Therefore the third region,
            // region2, isn't used.
            channel.Region2Start = 576;
        }
        else
        {
            // If window switching is not used, the block type is always Long.
            channel.BlockType = BlockType.Long;

            for (var i = 0; i < 3; i++)
            {
                channel.TableSelect[i] = (byte)bs.ReadBitsLeq32(5);
            }

            // When window switching is not used, only LONG scale-factor bands are used for each region.
            // The number of bands in region0 and region1 are defined in side_info. The stored value is
            // 1 less than the actual value.
            var region0Count = (byte)bs.ReadBitsLeq32(4) + 1;
            var region01Count = (byte)bs.ReadBitsLeq32(3) + 1 + region0Count;

            channel.Region1Start = SFB_LONG_BANDS[header.SampleRateIdx][region0Count];

            // The count in region0_1_count may exceed the last band (22) in the LONG bands table.
            // Protect against this.
            channel.Region2Start = region01Count switch
            {
                > 0 and <= 22 => SFB_LONG_BANDS[header.SampleRateIdx][region01Count],
                _ => 576
            };
        }

        // For MPEG2, preflag is determined implicitly when reading the scale factors.
        channel.Preflag = header.Version is MpegVersion.Mpeg1 ? bs.ReadBool() : false;

        channel.ScalefacScale = bs.ReadBool();
        channel.Count1TableSelect = (byte)bs.ReadBit();

        return;
    }

    /// <summary>
    /// Reads the scale factors for a single channel in a granule in a MPEG version 1 audio frame.
    /// </summary>
    /// <param name="bs"></param>
    /// <param name="gr"></param>
    /// <param name="ch"></param>
    /// <param name="frameDate"></param>
    /// <returns></returns>
    /// <exception cref="NotImplementedException"></exception>
    public static uint ReadScaleFactorsMpeg1(BitReaderLtr bs, int gr, int ch, FrameData frameDate)
    {
        var bitsRead = 0;

        var channel = frameDate.Granules[gr].Channels.Span[ch];

        // For MPEG1, scalefac_compress is a 4-bit index into a scale factor bit length lookup table.
        var (slen1, slen2) = SCALE_FACTOR_SLEN[channel.ScalefacCompress];

        // Short or Mixed windows...
        if (channel.BlockType is BlockType.Short or BlockType.ShortMixed)
        {
            var isMixed = channel.BlockType is BlockType.ShortMixed;

            // If the block is mixed, there are three total scale factor partitions. The first is a long
            // scale factor partition for bands 0..8 (scalefacs[0..8] with each scale factor being slen1
            // bits long. Following this is a short scale factor partition covering bands 8..11 with a
            // window of 3 (scalefacs[8..17]) and each scale factoring being slen1 bits long.
            //
            // If a block is not mixed, then there are a total of two scale factor partitions. The first
            // is a short scale factor partition for bands 0..6 with a window length of 3
            // (scalefacs[0..18]) and each scale factor being slen1 bits long.
            var nSfb = isMixed ? (8 + 3 * 3) : (6 * 3);

            if (slen1 > 0)
            {
                for (var i = 0; i < nSfb; i++)
                {
                    channel.ScaleFactors[i] = (byte)bs.ReadBitsLeq32(slen1);
                }

                bitsRead += (int)(nSfb * slen1);
            }

            // The final scale factor partition is always a a short scale factor window. It covers bands
            // 11..17 (scalefacs[17..35]) if the block is mixed, or bands 6..12 (scalefacs[18..36]) if
            // not. Each band has a window of 3 with each scale factor being slen2 bits long.
            if (slen2 > 0)
            {
                for (var i = nSfb; i < (nSfb + (6 * 3)); i++)
                {
                    channel.ScaleFactors[i] = (byte)bs.ReadBitsLeq32(slen2);
                }

                bitsRead += (int)((6 * 3) * slen2);
            }
        }
        else
        {
            // Normal (long, start, end) windows...

            // For normal windows there are 21 scale factor bands. These bands are divivided into four
            // band ranges. Scale factors in the first two band ranges: [0..6], [6..11], have scale
            // factors that are slen1 bits long, while the last two band ranges: [11..16], [16..21] have
            // scale factors that are slen2 bits long.

            for (int i = 0; i < SCALE_FACTOR_BANDS.Length; i++)
            {
                var (start, end) = SCALE_FACTOR_BANDS[i];

                var slen = i < 2 ? slen1 : slen2;

                // If this is the second granule, and the scale factor selection information for this
                // channel indicates that the scale factors should be copied from the first granule,
                // do so.
                if (gr > 0 && frameDate.Scfsi[ch][i])
                {
                    var granule0 = frameDate.Granules[0];
                    var granule1 = frameDate.Granules.AsSpan()[1..];

                    var to = granule1[0].Channels.Span[ch].ScaleFactors.AsSpan()
                        [start..end];
                    granule0.Channels.Span[ch].ScaleFactors.AsSpan()
                        [start..end].CopyTo(to);
                }
                // Otherwise, read the scale factors from the bitstream. Since scale factors are already
                // zeroed out by default, don't do anything if slen is 0.
                else if (slen > 0)
                {
                    for(int sfb = start; start < end; start++)
                    {
                        frameDate.Granules[gr].Channels.Span[ch].ScaleFactors[sfb]
                            = (byte)bs.ReadBitsLeq32(slen);
                    }
                    bitsRead += (int)((end - start) * slen);
                }
            }
        }

        return (uint)bitsRead;
    }

    static (int, int)[] SCALE_FACTOR_BANDS = new (int, int)[]
    {
        (0, 6),
        (6, 11),
        (11, 16),
        (16, 21)
    };
}