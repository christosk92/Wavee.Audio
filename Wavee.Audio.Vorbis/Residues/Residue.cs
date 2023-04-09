using Wavee.Audio.IO;
using Wavee.Audio.Vorbis.Decoding;
using Wavee.Audio.Vorbis.Decoding.DspState;

namespace Wavee.Audio.Vorbis.Residues;

internal sealed class Residue
{
    private readonly ResidueSetup _setup;
    private byte[] _partClasses;
    private float[] _type2Buf;

    private Residue(ResidueSetup setup, byte[] partClasses, float[] type2Buf)
    {
        _setup = setup;
        _partClasses = partClasses;
        _type2Buf = type2Buf;
    }

    public static Residue Read(BitReaderRtl bs, ushort residueType, byte maxCodebook)
    {
        var setup = ReadSetup(bs, residueType, maxCodebook);

        return new Residue(setup, Array.Empty<byte>(), Array.Empty<float>());
    }

    private static ResidueSetup ReadSetup(BitReaderRtl bs, ushort residueType, byte maxCodebook)
    {
        var residueBegin = bs.ReadBitsLeq32(24);
        var residueEnd = bs.ReadBitsLeq32(24);
        var residuePartitionSize = bs.ReadBitsLeq32(24) + 1;
        var residueClassifications = (byte)(bs.ReadBitsLeq32(6) + 1);
        var residueClassbook = (byte)bs.ReadBitsLeq32(8);

        if (residueEnd < residueBegin)
            throw new NotSupportedException("Vorbis residue end is less than residue begin.");

        var residueVqBooks = new List<ResidueVqClass>();

        for (int i = 0; i < residueClassifications; i++)
        {
            var lowBits = (byte)bs.ReadBitsLeq32(3);
            var highbits = bs.ReadBool() ? (byte)bs.ReadBitsLeq32(5) : (byte)0;

            //            let is_used = (high_bits << 3) | low_bits;
            var isUsed = (byte)((highbits << 3) | lowBits);

            residueVqBooks.Add(new ResidueVqClass(isUsed));
        }

        var residueMaxPass = 0;

        foreach (var vqBooks in residueVqBooks)
        {
            // For each set of residue codebooks, if the codebook is used, read the codebook
            // number.
            for (int j = 0; j < vqBooks.Books.Length; j++)
            {
                var book = vqBooks.Books[j];
                var isCodebookUsed = (vqBooks.IsUsed & (1 << j)) != 0;

                if (isCodebookUsed)
                {
                    //Read the codebook number
                    book = (byte)bs.ReadBitsLeq32(8);

                    if (book == 0 || book >= maxCodebook)
                        throw new NotSupportedException("Vorbis residue codebook index is invalid.");

                    vqBooks.Books[j] = book;
                    residueMaxPass = Math.Max(residueMaxPass, j);
                }
            }
        }

        var setup = new ResidueSetup
        {
            ResidueType = residueType,
            ResidueBegin = residueBegin,
            ResidueEnd = residueEnd,
            ResiduePartitionSize = residuePartitionSize,
            ResidueClassifications = residueClassifications,
            ResidueClassbook = residueClassbook,
            ResidueVqClass = residueVqBooks,
            ResidueMaxPass = residueMaxPass
        };

        return setup;
    }

    public void ReadResidue(BitReaderRtl bs, byte bsExp, VorbisCodebook[] codebooks, BitSet256 residueChannels,
        DspChannel[] channels)
    {
        try
        {
            if (_setup.ResidueType == 2)
            {
                ReadResidueInnerType2(bs, bsExp, codebooks, residueChannels, channels);
            }
            else
            {
                ReadResidueInnerType01(bs, bsExp, codebooks, residueChannels, channels);
            }
        }
        // An end-of-bitstream error is classified under ErrorKind::Other. This condition
        // should not be treated as an error.
        catch (IOException x)
        {
            if (x is not EndOfStreamException)
                throw;
        }

        // For format 2, the residue vectors for all channels are interleaved together into one
        // large vector. This vector is in the scratch-pad buffer and can now be de-interleaved
        // into the channel buffers.
        if (_setup.ResidueType == 2)
            Deinterleave2(residueChannels, channels);
    }

    private void ReadResidueInnerType01(BitReaderRtl bs,
        byte bsExp,
        VorbisCodebook[] codebooks,
        BitSet256 residueChannels,
        DspChannel[] channels)
    {
        throw new NotImplementedException();
    }

    private void ReadResidueInnerType2(BitReaderRtl bs,
        byte bsExp,
        VorbisCodebook[] codebooks,
        BitSet256 residueChannels, DspChannel[] channels)
    {
        var classbook = codebooks[_setup.ResidueClassbook];

        // The actual length of the entire residue vector for a channel (formats 0 and 1), or all
        // interleaved channels (format 2).
        var fullResidueLength = ((1 << bsExp) >> 1) * residueChannels.Count();

        // The range of the residue vector being decoded.
        var limitResidueBegin = Math.Min(_setup.ResidueBegin, fullResidueLength);
        var limitResidueEnd = Math.Min(_setup.ResidueEnd, fullResidueLength);

        // Length of the decoded part of the residue vector.
        var residueLen = limitResidueEnd - limitResidueBegin;

        // The number of partitions in the residue vector.
        var partsPerClassword = classbook.CodebooksDimensions;

        //Number of partitions to read
        var partsToRead = residueLen / _setup.ResiduePartitionSize;

        // Reserve partition classification space
        PreparePartitionClassifications(partsToRead);

        // reserve type 2 interleave buffer storage and zero all samples
        PrepareType2FormatBuffer(fullResidueLength);

        // If all channels are marked do-not-decode then exit immediately.
        var hasChannelToDecode
            = residueChannels.Select((_, i) => i).Any(c => !channels[c].DoNotDecode);
        if (!hasChannelToDecode)
            return;

        var partSize = _setup.ResiduePartitionSize;

        // Residues may be encoded in up-to 8 passes. Fewer passes may be encoded by prematurely
        // "ending" the packet. This means that an end-of-bitstream error is actually NOT an error.
        for (int pass = 0; pass < (_setup.ResidueMaxPass + 1); pass++)
        {
            // Iterate over the partitions in batches grouped by classword.
            for (int partFirst = 0; partFirst < partsToRead; partFirst += (int)partsPerClassword)
            {
                // The class assignments for each partition in the classword group are only
                // encoded in the first pass.
                if (pass == 0)
                {
                    classbook.TryReadScalar(bs, out var code);

                    DecodeClasses(
                        code,
                        partsPerClassword,
                        _setup.ResidueClassifications,
                        _partClasses.AsSpan(partFirst)
                    );
                }

                // The class assignments for each partition in the classword group are only
                // encoded in the first pass.
                var partLast = Math.Min(partsToRead, partFirst + partsPerClassword);

                // Iterate over all partitions belonging to the current classword group.
                for (int part = partFirst; part < partLast; part++)
                {
                    var vqClass = _setup.ResidueVqClass[_partClasses[part]];

                    if (vqClass.IsUsedForPass(pass))
                    {
                        var vqbook = codebooks[vqClass.Books[pass]];
                        var partStart = limitResidueBegin + part * partSize;

                        // Residue type 2 is implemented in term of type 1.
                        ReadResiduePartitionFormat1(
                            bs,
                            vqbook,
                            _type2Buf.AsSpan(partStart..(partSize + partStart))
                        );
                    }
                }
            }
            // End of partition batch iteration.
        }
        // End of pass iteration.
    }

    private static void ReadResiduePartitionFormat1(BitReaderRtl bs, VorbisCodebook vqbook, Span<float> output)
    {
        var dim = vqbook.CodebooksDimensions;

        // For small dimensions it is too expensive to use iterator loops. Special case small sizes
        // to improve performance.
        switch (dim)
        {
            case 2:
            {
                for (int i = 0; i < output.Length; i += 2)
                {
                    vqbook.TryReadVector(bs, out var vq);

                    // Amortize the cost of the bounds check.
                    output[0 + i] += vq[0];
                    output[1 + i] += vq[1];
                }

                break;
            }
            case 4:
            {
                for (int i = 0; i < output.Length; i += 4)
                {
                    vqbook.TryReadVector(bs, out var vq);

                    // Amortize the cost of the bounds check.
                    output[0 + i] += vq[0];
                    output[1 + i] += vq[1];
                    output[2 + i] += vq[2];
                    output[3 + i] += vq[3];
                }

                break;
            }
            default:
            {
                for (int i = 0; i < output.Length; i += dim)
                {
                    vqbook.TryReadVector(bs, out var vq);

                    // Ensure that the chunk size is correct
                    if (vq.Length != dim)
                    {
                        throw new InvalidOperationException("Invalid VQ length.");
                    }

                    for (int j = 0; j < dim && i + j < output.Length; j++)
                    {
                        output[i + j] += vq[j];
                    }
                }

                break;
            }
        }
    }

    private void DecodeClasses(uint val, uint partsPerClassword, ushort classifications, Span<byte> output)
    {
        //The number of partitions that need a class assignment
        var numParts = output.Length;

        // If the number of partitions per classword is greater than the number of partitions that need
        // a class assignment, then the excess classes must be dropped because class assignments are
        // assigned in reverse order.
        int skip = 0;
        if (partsPerClassword > numParts)
        {
            skip = (int)(partsPerClassword - numParts);

            for (int i = 0; i < skip; i++)
            {
                val /= classifications;
            }
        }

        for (int i = (int)(partsPerClassword - skip - 1); i >= 0; i--)
        {
            output[i] = (byte)(val % classifications);
            val /= classifications;
        }
    }

    private void PrepareType2FormatBuffer(int len)
    {
        if (_type2Buf.Length < len)
        {
            // for (int i = _type2Buf.Length; i < len; i++)
            // {
            //     _type2Buf.Add(0);
            // }
            var oldLen = _type2Buf.Length;
            Array.Resize(ref _type2Buf, len);
            for (int i = oldLen; i < len; i++)
            {
                _type2Buf[i] = 0;
            }
        }

        for (int i = 0; i < len; i++)
        {
            _type2Buf[i] = 0;
        }
    }

    private void PreparePartitionClassifications(int len)
    {
        if (_partClasses.Length < len)
        {
            var oldLen = _partClasses.Length;
            Array.Resize(ref _partClasses, len);
            for (int i = oldLen; i < len; i++)
            {
                _partClasses[i] = 0;
            }
        }
    }

    private void Deinterleave2(BitSet256 residueChannels, DspChannel[] channels)
    {
        var count = residueChannels.Count();
        switch (count)
        {
            case 2:
            {
                // Two channel deinterleave.
                // Two channel deinterleave.
                int ch0, ch1;
                (ch0, ch1) = GetFirstTwoChannelIndices(residueChannels);

                DspChannel channel0 = channels[ch0];
                DspChannel channel1 = channels[ch1];

                // Deinterleave.
                for (int i = 0; i < _type2Buf.Length; i += 2)
                {
                    channel0.Residue[i / 2] = _type2Buf[i];
                    channel1.Residue[i / 2] = _type2Buf[i + 1];
                }

                break;
            }
        }
    }

    // Helper function to get the indices of the first two channels in the residue.
    private (int, int) GetFirstTwoChannelIndices(BitSet256 residueChannels)
    {
        int? ch0 = null, ch1 = null;

        foreach (int ch in residueChannels)
        {
            if (ch0 == null)
            {
                ch0 = ch;
            }
            else if (ch1 == null)
            {
                ch1 = ch;
                break;
            }
        }

        if (ch0.HasValue && ch1.HasValue)
        {
            return (ch0.Value, ch1.Value);
        }

        throw new InvalidOperationException("Not enough channel indices in the residue.");
    }
}

internal class ResidueVqClass
{
    public ResidueVqClass(byte isUsed)
    {
        IsUsed = isUsed;
        Books = new byte[8];
        for (int i = 0; i < Books.Length; i++)
            Books[i] = 0;
    }

    public byte[] Books { get; }
    public byte IsUsed { get; }

    public bool IsUsedForPass(int pass)
    {
        return (IsUsed & (1 << pass)) != 0;
    }
}

internal class ResidueSetup
{
    public ushort ResidueType { get; set; }
    public int ResidueBegin { get; set; }
    public int ResidueEnd { get; set; }
    public int ResiduePartitionSize { get; set; }
    public byte ResidueClassifications { get; set; }
    public byte ResidueClassbook { get; set; }
    public List<ResidueVqClass> ResidueVqClass { get; set; }
    public int ResidueMaxPass { get; set; }
}