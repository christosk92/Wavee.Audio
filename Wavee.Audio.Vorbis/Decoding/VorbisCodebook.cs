using System.Diagnostics;
using Wavee.Audio.IO;
using Wavee.Audio.Vorbis.Decoding.Codebooks;

namespace Wavee.Audio.Vorbis.Decoding;

internal class VorbisCodebook
{
    private VorbisCodebook(Codebook codebook, ushort codebooksDimensions, float[]? vqVec)
    {
        Codebook = codebook;
        CodebooksDimensions = codebooksDimensions;
        VqVec = vqVec;
    }

    public Codebook Codebook { get; }
    public ushort CodebooksDimensions { get; }
    public float[]? VqVec { get; }

    public static VorbisCodebook Read(BitReaderRtl bs)
    {
        // Verify codebook synchronization word.
        var sync = bs.ReadBitsLeq32(24);

        if (sync != 0x564342)
            throw new InvalidDataException("Invalid codebook synchronization word.");

        // Read codebook number of dimensions and entries.
        var codebooksDimensions = (ushort)bs.ReadBitsLeq32(16);
        var codebooksEntries = (ushort)bs.ReadBitsLeq32(24);

        //ordered flag
        var isLengthOrdered = bs.ReadBool();

        var codeLens = new List<byte>(codebooksEntries);

        if (!isLengthOrdered)
        {
            // Codeword list is not length ordered.
            var isSparse = bs.ReadBool();

            if (isSparse)
            {
                for (var i = 0; i < codebooksEntries; i++)
                {
                    var isUsed = bs.ReadBool();

                    byte codeLen = 0;
                    if (isUsed)
                    {
                        codeLen = (byte)(bs.ReadBitsLeq32(5) + 1);
                    }

                    codeLens.Add(codeLen);
                }
            }
            else
            {
                // Densely packed codeword entry list.
                for (var i = 0; i < codebooksEntries; i++)
                {
                    var codeLen = (byte)(bs.ReadBitsLeq32(5) + 1);
                    codeLens.Add(codeLen);
                }
            }
        }
        else
        {
            // Codeword list is length ordered.
            uint curEntry = 0;
            var curLen = (byte)(bs.ReadBitsLeq32(5) + 1);

            while (true)
            {
                uint numBits = 0;
                if (codebooksEntries > curEntry)
                {
                    numBits = (codebooksEntries - curEntry).ILog();
                }

                var num = bs.ReadBitsLeq32(numBits);
                codeLens.AddRange(Enumerable.Repeat(curLen, num));

                curLen += 1;
                curEntry += (uint)num;

                if (curEntry > codebooksEntries)
                {
                    throw new InvalidDataException("Invalid codebook entry count.");
                }

                if (curEntry == codebooksEntries)
                    break;
            }
        }

        // Read and unpack vector quantization (VQ) lookup table.
        var lookupType = bs.ReadBitsLeq32(4);

        var vq_vec = (lookupType & 0xf) switch
        {
            0 => null,
            1 or 2 => ReadLookupTable(lookupType, bs, codebooksEntries,
                codebooksDimensions,
                codeLens),
        };

        // Generate a canonical list of codewords given the set of codeword lengths.
        var codeWords = SynthesizeCodewords(codeLens);

        // Generate the values associated for each codeword.
        // TODO: Should unused entries be 0 or actually the correct value?
        var values = Enumerable.Range(0, codebooksEntries).Select(i => (uint)i).ToArray();

        // Finally, generate the codebook with a reverse (LSb) bit order.
        var builder = CodebookBuilder.NewSparse(BitOrder.Reverse);

        //Read in 8-bit values
        builder.SetMaxBitsPerBlock(8);

        var codebook = builder.Make(
            codeWords,
            codeLens,
            values
        );

        return new VorbisCodebook(
            codebook,
            codebooksDimensions,
            vq_vec
        );
    }

    private static uint[] SynthesizeCodewords(List<byte> codeLens)
    {
        // This codeword generation algorithm works by maintaining a table of the next valid codeword for
        // each codeword length.
        //
        // Consider a huffman tree. Each level of the tree correlates to a specific length of codeword.
        // For example, given a leaf node at level 2 of the huffman tree, that codeword would be 2 bits
        // long. Therefore, the table being maintained contains the codeword that would identify the next
        // available left-most node in the huffman tree at a given level. Therefore, this table can be
        // interrogated to get the next codeword in a simple lookup and the tree will fill-out in the
        // canonical order.
        //
        // Note however that, after selecting a codeword, C, of length N, all codewords of length > N
        // cannot use C as a prefix anymore. Therefore, all table entries for codeword lengths > N must
        // be updated such that these codewords are skipped over. Likewise, the table must be updated for
        // lengths < N to account for jumping between nodes.
        //
        // This algorithm is a modified version of the one found in the Vorbis reference implementation.

        var codewords = new List<uint>();
        Span<uint> nextCodeword = new uint[33];
        int numSparse = 0;

        foreach (byte len in codeLens)
        {
            Debug.Assert(len <= 32);

            if (len == 0)
            {
                numSparse += 1;
                codewords.Add(0);
                continue;
            }

            int codewordLen = len;

            uint codeword = nextCodeword[codewordLen];

            if (len < 32 && (codeword >> len) > 0)
            {
                throw new InvalidOperationException("Codebook overspecified");
            }

            for (int i = codewordLen; i >= 0; i--)
            {
                if ((nextCodeword[i] & 1) == 1)
                {
                    nextCodeword[i] = nextCodeword[i - 1] << 1;
                    break;
                }

                nextCodeword[i] += 1;
            }

            uint branch = nextCodeword[codewordLen];

            for (int i = 1; i < nextCodeword.Length - codewordLen; i++)
            {
                if (nextCodeword[codewordLen + i] == codeword << i)
                {
                    nextCodeword[codewordLen + i] = branch << i;
                }
                else
                {
                    break;
                }
            }

            codewords.Add(codeword);
        }

        // Check that the tree is fully specified and complete. This means that the next codeword for
        // codes of length 1 to 32, inclusive, are saturated.

        // Single entry codebooks are technically invalid, but must be supported as a special-case
        // per Vorbis I specification, errate 20150226.
        var isSingleEntryCodebook = codeLens.Count - numSparse == 1;

        static bool IsUnderSpecified(ReadOnlySpan<uint> codebook)
        {
            /*
             *  var isUnderspecified = nextCodeword[1..]
               .Select((c, i) => (c, i + 1))
               .Any(pair => (pair.c & (uint.MaxValue >> (32 - pair.Item2))) != 0);
             */
            var enumerator = codebook[1..].GetEnumerator();
            int index = 0;
            while (enumerator.MoveNext())
            {
                var c = enumerator.Current;
                int i = index + 1;
                if ((c & (uint.MaxValue >> (32 - i))) != 0)
                {
                    return true;
                }

                index++;
            }

            return false;
        }

        var isUnderspecified = IsUnderSpecified(nextCodeword);
        if (isUnderspecified && !isSingleEntryCodebook)
        {
            throw new InvalidOperationException("The Huffman tree is not fully specified and complete.");
        }


        return codewords.ToArray();
    }

    private static float[] ReadLookupTable(
        int lookupType,
        BitReaderRtl bs,
        ushort codebooksEntries,
        ushort codebookDimensions,
        List<byte> codeLens)
    {
        var readMinValue = (uint)bs.ReadBitsLeq32(32);
        var minValue = (readMinValue).Float32Unpack();

        var readDeltaValue = (uint)bs.ReadBitsLeq32(32);
        var deltaValue = (readDeltaValue).Float32Unpack();
        var valueBits = (byte)bs.ReadBitsLeq32(4) + 1;
        var sequenceP = bs.ReadBool();

        var lookupValues = lookupType switch
        {
            1 => Lookup1Values(codebooksEntries, codebookDimensions),
            2 => (uint)(codebooksEntries * codebookDimensions),
            _ => throw new InvalidDataException("Invalid lookup type.")
        };

        var multiplicands = new ushort[lookupValues];
        for (var i = 0; i < lookupValues; i++)
        {
            multiplicands[i] = (ushort)bs.ReadBitsLeq32((uint)valueBits);
        }

        var lookupTable = new float[codebooksEntries * codebookDimensions];

        switch (lookupType)
        {
            case 1:
                for (var idx = 0; idx < codebooksEntries; idx++)
                {
                    var last = 0.0;
                    var idxDiv = 1;
                    for (var i = 0; i < codebookDimensions; i++)
                    {
                        var moff = (idx / idxDiv) % lookupValues;
                        var value = (float)multiplicands[moff] * deltaValue + minValue + last;
                        lookupTable[idx * codebookDimensions + i] = (float)value;

                        if (sequenceP) last = value;

                        idxDiv *= (int)lookupValues;
                    }
                }

                break;
            case 2:
                for (var idx = 0; idx < codebooksEntries; idx++)
                {
                    var last = 0.0;
                    var moff = idx * codebookDimensions;
                    for (var i = 0; i < codebookDimensions; i++)
                    {
                        var value = multiplicands[moff] * deltaValue + minValue + last;
                        lookupTable[idx * codebookDimensions + i] = (float)value;

                        if (sequenceP) last = value;
                        ++moff;
                    }
                }

                break;
            default:
                throw new InvalidDataException("Invalid lookup type.");
        }

        return lookupTable;
    }

    private static uint Lookup1Values(ushort codebooksEntries, ushort codebookDimensions)
    {
        var r = (uint)Math.Floor(Math.Exp(Math.Log(codebooksEntries) / codebookDimensions));

        if (Math.Floor(Math.Pow(r + 1, codebookDimensions)) <= codebooksEntries) ++r;

        return r;
    }

    public void TryReadScalar(BitReaderRtl bs, out uint cval)
    {
        // An entry in a scalar codebook is just the value.
        try
        {
            var vals = bs.ReadCodebook(Codebook);
            cval = vals.Item1;
        }
        catch (EndOfStreamException)
        {
            cval = 0;
        }
    }

    public void TryReadVector(BitReaderRtl bs, out float[] p1)
    {
        // An entry in a VQ codebook is the index of the VQ vector.
        var entry = bs.ReadCodebook(Codebook).Item1;

        if (VqVec is not null)
        {
            var dim = this.CodebooksDimensions;
            var start = entry * dim;

            p1 = VqVec[(int)start..(int)(start + dim)];
            return;
        }

        throw new InvalidOperationException("VQ vector is null.");
    }
}