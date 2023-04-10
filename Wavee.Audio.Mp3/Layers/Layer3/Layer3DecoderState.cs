using Wavee.Audio.Audio;
using Wavee.Audio.Helpers.Extensions;
using Wavee.Audio.IO;
using Wavee.Audio.Mp3.Frame;
using Wavee.Audio.Mp3.Header;

namespace Wavee.Audio.Mp3.Layers.Layer3;

internal sealed class Layer3DecoderState :
    IMpaDecoderState
{
    public float[][][] Samples { get; set; }
    public float[][][] Overlap { get; set; }
    public SynthesisState[] Synthesis { get; set; }
    public BitReservoir Reservoir { get; set; }

    public Layer3DecoderState()
    {
        Samples = new float[2][][];
        for (int i = 0; i < 2; i++)
        {
            Samples[i] = new float[2][];
            for (int j = 0; j < 2; j++)
            {
                Samples[i][j] = new float[576];
            }
        }

        Overlap = new float[2][][];
        for (int i = 0; i < 2; i++)
        {
            Overlap[i] = new float[32][];
            for (int j = 0; j < 32; j++)
            {
                Overlap[i][j] = new float[18];
            }
        }

        Synthesis = new SynthesisState[2];
        Reservoir = new BitReservoir();
    }

    public void Decode(BufReader reader, FrameHeader header, AudioBuffer<float> output)
    {
        // Initialize an empty FrameData to store the side_info and main_data portions of the
        // frame.
        var frameDate = new FrameData();

        var _crc = header.HasCrc ? reader.ReadUShortBE() : (ushort?)null;

        var buf = reader.ReadBufBytesAvailable();

        var bs = new BitReaderLtr(buf);

        // Read side_info into the frame data.
        // TODO: Use a MonitorStream to compute the CRC.
        int len = 0;
        try
        {
            len = Bitstream.ReadSideInfo(bs, header, frameDate);
        }
        catch (System.Exception x)
        {
            Reservoir.Clear();
            throw;
        }

        // Buffer main data into the bit resevoir.
        var underflow = Reservoir.Fill(buf[len..].Span, frameDate.MainDataBegin);

        // Read the main data (scale factors and spectral samples).
        try
        {
            var l = ReadMainData(header, 8 * underflow, frameDate);
            // Consume the bytes of main data read from the resevoir.
            Reservoir.Consume(l);
        }
        catch (System.Exception x)
        {
            // The bit reservoir was likely filled with invalid data. Clear it for the next
            // packet.
            Reservoir.Clear();
            throw;
        }
    }

    private int ReadMainData(FrameHeader header, uint underflowBits, FrameData frameDate)
    {
        var mainData = Reservoir.BytesRef();
        var part23Begin = 0;
        uint part23Skipped = 0;

        for (int gr = 0; gr < header.NGranules(); gr++)
        {
            // If the resevoir underflowed (i.e., main_data_begin references bits not present in the
            // resevoir) then skip the granule(s) the missing bits would belong to.
            if (part23Skipped < underflowBits)
            {
                // Zero the samples in the granule channel(s) and sum the part2/3 bits that were
                // skipped.
                for (int ch = 0; ch < header.NumberOfChannels(); ch++)
                {
                    Requantize.Zero(Samples[gr][ch]);
                    part23Skipped += frameDate.Granules[gr].Channels.Span[ch].Part2_3Length;
                }

                // Adjust the start position of the next granule in the buffer of main data that is
                // available.
                if (part23Skipped > underflowBits)
                {
                    part23Begin = (int)(part23Skipped - underflowBits);
                }

                continue;
            }

            for (int ch = 0; ch < header.NumberOfChannels(); ch++)
            {
                var byteIndex = part23Begin >> 3;

                // Create a bit reader at the expected starting bit position.
                if (byteIndex > mainData.Length)
                {
                    throw new System.Exception("Invalid main data byte index.");
                }

                var bs = new BitReaderLtr(mainData[byteIndex..].ToArray());
                var bitIndex = part23Begin & 0x7;

                if (bitIndex > 0)
                {
                    bs.IgnoreBits((uint)bitIndex);
                }

                // Read the scale factors (part2) and get the number of bits read.
                uint part2Len = 0;
                if (header.Version is MpegVersion.Mpeg1)
                {
                    part2Len = Bitstream.ReadScaleFactorsMpeg1(bs, gr, ch, frameDate);
                }
                else
                {
                    throw new System.Exception("MPEG-2 not supported.");
                    // Bitstream.ReadScaleFactorsMpeg2(bs,
                    //     ch > 0 && header.IsIntensityStereo(),
                    //     frameDate.Granules[gr].Channels.Span[ch]
                    // );
                }

                var part23Len = (uint)frameDate.Granules[gr].Channels.Span[ch].Part2_3Length;

                // The part2 length must be less than or equal to the part2_3_length.
                if (part2Len > part23Len)
                {
                    throw new System.Exception("Invalid part2 length.");
                }

                // The Huffman code length (part3).
                var part3len = part23Len - part2Len;

                // Decode the Huffman coded spectral samples and get the starting index of the rzero
                // partition.
                var huffmanResult = Requantize
                    .ReadHuffmanSamples(
                        bs,
                        frameDate.Granules[gr].Channels.Span[ch],
                        part3len,
                        Samples[gr][ch]
                    );
            }
        }

        throw new System.Exception("Not implemented.");
    }
}

internal class SynthesisState
{
}