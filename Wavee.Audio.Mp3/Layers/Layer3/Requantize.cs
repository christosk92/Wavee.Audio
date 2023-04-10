using Wavee.Audio.IO;
using Wavee.Audio.Mp3.Layers.Layer3.Codebook;

namespace Wavee.Audio.Mp3.Layers.Layer3;

internal static class Requantize
{
    /// <summary>
    /// Lookup table for computing x(i) = s(i)^(4/3) where s(i) is a decoded Huffman sample. The
    /// value of s(i) is bound between 0..8207.
    /// </summary>
    private static float[] REQUANTIZE_POW43;

    static Requantize()
    {
        // It is wasteful to initialize to 0.. however, Symphonia policy is to limit unsafe code to
        // only symphonia-core.
        //
        // TODO: Implement generic lookup table initialization in the core library.
        var pow43 = new float[8207];
        for (int i = 0; i < 8207; i++)
        {
            pow43[i] = (float)Math.Pow(i, 4.0 / 3.0);
        }

        REQUANTIZE_POW43 = pow43;
    }

    public static void Zero(float[] floats)
    {
        for (int i = 0; i < floats.Length; i++)
        {
            floats[i] = 0;
        }
    }

    /// <summary>
    /// Reads the Huffman coded spectral samples for a given channel in a granule from a `BitStream`
    /// into a provided sample buffer. Returns the number of decoded samples (the starting index of the
    /// rzero partition).
    ///
    /// Note, each spectral sample is raised to the (4/3)-rd power. This is not actually part of the
    /// Huffman decoding process, but, by converting the integer sample to floating point here we don't
    /// need to do pointless casting or use an extra buffer.
    /// </summary>
    /// <param name="bs"></param>
    /// <param name="granuleChannel"></param>
    /// <param name="part3Len"></param>
    /// <param name="floats"></param>
    /// <returns></returns>
    /// <exception cref="NotImplementedException"></exception>
    public static object ReadHuffmanSamples(BitReaderLtr bs,
        GranuleChannel channel,
        uint part3Bits,
        Span<float> buf)
    {
        // If there are no Huffman code bits, zero all samples and return immediately.
        if (part3Bits == 0)
        {
            buf.Fill(0);
            return 0;
        }

        // Dereference the POW43 table once per granule since there is a tiny overhead each time a
        // lazy_static is dereferenced that should be amortized over as many samples as possible.
        var pow43Table = REQUANTIZE_POW43;

        var bitsRead = 0;
        var i = 0;

        // There are two samples per big_value, therefore multiply big_values by 2 to get number of
        // samples in the big_value partition.
        var bigValuesLen = channel.BigValues * 2;

        // There are up-to 3 regions in the big_value partition. Determine the sample index denoting the
        // end of each region (non-inclusive). Clamp to the end of the big_values partition.
        var regions = new int[]
        {
            Math.Min(channel.Region1Start, bigValuesLen),
            Math.Min(channel.Region2Start, bigValuesLen),
            Math.Min(576, bigValuesLen)
        };
        
        // Iterate over each region in big_values.
        for(int regionIdx = 0; regionIdx < regions.Length; regionIdx++)
        {
            var regionEnd = regions[regionIdx];
            
            // Select the Huffman table based on the region's table select value.
            var tableSelect = channel.TableSelect[regionIdx];
            
            // Tables 0..16 are all unique, while tables 16..24 and 24..32 each use one table but
            // differ in the number of linbits to use.
            // var codebook = tableSelect switch
            // {
            //     > 0 and < 16 => HuffmanCodebook.HUFFMAN_TABLES[tableSelect],
            //     > 16 and < 24 => HuffmanCodebook.HUFFMAN_TABLES[16],
            //     > 24 and < 32 => HuffmanCodebook.HUFFMAN_TABLES[24],
            //     _ => throw new NotImplementedException()
            // };
            //
            // var linBits = HuffmanCodebook.CODEBOOK_LINBITS[tableSelect];
            //
            
            
        }
        throw new NotImplementedException();
    }
}