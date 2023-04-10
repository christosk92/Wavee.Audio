namespace Wavee.Audio.Mp3.Layers.Layer3;

internal sealed class GranuleChannel
{
    public GranuleChannel()
    {
        Part2_3Length = 0;
        BigValues = 0;
        GlobalGain = 0;
        ScalefacCompress = 0;
        BlockType = BlockType.Long;
        SubBlockGain = new byte[3];
        for (var i = 0; i < 3; i++)
        {
            SubBlockGain[i] = 0;
        }

        TableSelect = new byte[3];
        for (var i = 0; i < 3; i++)
        {
            TableSelect[i] = 0;
        }

        Region1Start = 0;
        Region2Start = 0;
        Preflag = false;
        ScalefacScale = false;
        Count1TableSelect = 0;
        ScaleFactors = new byte[39];
        for (var i = 0; i < 39; i++)
        {
            ScaleFactors[i] = 0;
        }

        RZero = 0;
    }

    /// <summary>
    /// Total number of bits used for scale factors (part2) and Huffman encoded data (part3).
    /// </summary>
    public ushort Part2_3Length { get; set; }

    /// <summary>
    /// HALF the number of samples in the big_values partition (sum of all samples in
    /// `region[0..3]`).
    /// </summary>
    public ushort BigValues { get; set; }

    /// <summary>
    /// Logarithmic quantization step size.
    /// </summary>
    public byte GlobalGain { get; set; }

    /// <summary>
    /// Depending on the MPEG version, `scalefac_compress` determines how many bits are allocated
    /// per scale factor.
    ///
    /// - For MPEG1 bitstreams, `scalefac_compress` is a 4-bit index into
    ///  `SCALE_FACTOR_SLEN[0..16]` to obtain a number of bits per scale factor pair.
    ///
    /// - For MPEG2/2.5 bitstreams, `scalefac_compress` is a 9-bit value that decodes into
    /// `slen[0..3]` (referred to as slen1-4 in the standard) for the number of bits per scale
    /// factor, and depending on which range the value falls into, for which bands.
    /// </summary>
    public ushort ScalefacCompress { get; set; }

    /// <summary>
    /// Indicates the block type (type of window) for the channel in the granule.
    /// </summary>
    public BlockType BlockType { get; set; }

    /// <summary>
    /// Gain factors for region[0..3] in big_values. Each gain factor has a maximum value of 7
    /// (3 bits).
    /// </summary>
    public byte[] SubBlockGain { get; init; }

    /// <summary>
    /// The Huffman table to use for decoding `region[0..3]` of big_values.
    /// </summary>
    public byte[] TableSelect { get; init; }

    /// <summary>
    /// The index of the first sample in region1 of big_values.
    /// </summary>
    public int Region1Start { get; set; }

    /// <summary>
    /// The index of the first sample in region2 of big_values.
    /// </summary>
    public int Region2Start { get; set; }

    /// <summary>
    /// Indicates if the pre-emphasis amount for each scale factor band should be added on to each
    /// scale factor before requantization.
    /// </summary>
    public bool Preflag { get; set; }


    /// <summary>
    /// A 0.5x (false) or 1x (true) multiplier for scale factors.
    /// </summary>
    public bool ScalefacScale { get; set; }

    /// <summary>
    /// Use Huffman Quads table A (0) or B (1), for decoding the count1 partition.
    /// </summary>
    public byte Count1TableSelect { get; set; }

    /// <summary>
    /// Long (scalefac_l) and short (scalefac_s) window scale factor bands. Must be interpreted
    /// based on the block type of the granule.
    ///
    /// For `block_type == BlockType::Short { is_mixed: false }`:
    ///   - `scalefac_s[0..36]` -> `scalefacs[0..36]`
    ///
    /// For `block_type == BlockType::Short { is_mixed: true }`:
    ///   - `scalefac_l[0..8]`  -> `scalefacs[0..8]`
    ///   - `scalefac_s[0..27]` -> `scalefacs[8..35]`
    ///
    /// For `block_type != BlockType::Short { .. }`:
    ///   - `scalefac_l[0..21]` -> `scalefacs[0..21]`
    ///
    /// Note: The standard doesn't explicitly call it out, but for Short blocks, there are three
    ///       additional scale factors, `scalefacs[36..39]`, that are always 0 and are not
    ///       transmitted in the bitstream.
    ///
    /// For MPEG1, and MPEG2 without intensity stereo coding, a scale factor will not exceed 4 bits
    /// in length (maximum value 15). For MPEG2 with intensity stereo, a scale factor will not
    /// exceed 5 bits (maximum value 31) in length.
    /// </summary>
    public byte[] ScaleFactors { get; init; }

    /// <summary>
    /// The starting sample index of the rzero partition, or the count of big_values and count1
    /// samples.
    /// </summary>
    public int RZero { get; init; }
}

internal enum BlockType
{
    Long,
    Start,
    Short,
    ShortMixed,
    End
}