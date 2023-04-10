using System.Runtime.CompilerServices;
using Wavee.Audio.Codebook;
using Wavee.Audio.Helpers.Extensions;

namespace Wavee.Audio.Codebooks;

public class CodebookBuilder
{
    private byte _maxBitsPerBlock;
    private BitOrder _bitOrder;
    private bool _isSparse;

    private CodebookBuilder(byte maxBitsPerBlock, BitOrder bitOrder, bool isSparse)
    {
        _bitOrder = bitOrder;
        _isSparse = isSparse;
        _maxBitsPerBlock = maxBitsPerBlock;
    }

    public static CodebookBuilder NewSparse(BitOrder order)
    {
        return new CodebookBuilder(4, order, true);
    }

    public void SetMaxBitsPerBlock(byte maxBitsPerBlock)
    {
        _maxBitsPerBlock = maxBitsPerBlock;
    }

    /// <summary>
    /// Construct a <see cref="Codebook"/> using the given codewords, their respective lengths, and values.
    ///
    /// This function may fail if the provided codewords do not form a complete VLC tree, or if
    /// the <see cref="CodebookEntry"/> is undersized.
    ///
    /// This function will panic if the number of code words, code lengths, and values differ.
    /// </summary>
    /// <param name="codeWords"></param>
    /// <param name="codeLens"></param>
    /// <param name="values"></param>
    /// <returns></returns>
    /// <exception cref="NotImplementedException"></exception>
    public Codebook<TValueType, TOffsetType> Make<TEntry, TValueType, TOffsetType>(uint[] codeWords,
        List<byte> codeLens,
        TValueType[] values) where TEntry : ICodebookEntry<TValueType, TOffsetType>
        where TOffsetType : unmanaged
        where TValueType : unmanaged
    {
        if (codeWords.Length != codeLens.Count || codeWords.Length != values.Length)
        {
            throw new ArgumentException("The number of code words, code lengths, and values must be the same.");
        }

        var blocks = new List<CodebookBlock<TValueType>>();

        byte maxCodeLen = 0;

        // Only attempt to generate something if there are code words.
        if (codeWords.Any())
        {
            //                let prefix_mask = !(!0 << self.max_bits_per_block);
            var prefixMask = ~((~0U << _maxBitsPerBlock));

            // Push a root block.
            blocks.Add(new CodebookBlock<TValueType>());

            // Populate the tree
            foreach ((var (code, codeLen), var value) in codeWords
                         .Zip(codeLens)
                         .Zip(values))
            {
                var parentBlocKid = 0;
                var len = codeLen;

                // A zero length codeword in a spare codebook is allowed, but not in a regular
                // codebook.
                if (codeLen == 0)
                {
                    if (_isSparse)
                        continue;
                    else
                    {
                        throw new NotImplementedException(
                            "A zero length codeword in a regular codebook is not supported.");
                    }
                }

                while (len > _maxBitsPerBlock)
                {
                    len -= _maxBitsPerBlock;

                    var prefix = (ushort)((code >> len) & prefixMask);

                    // Recurse down the tree.
                    if (blocks[parentBlocKid].Nodes.TryGetValue(prefix, out var blockId))
                    {
                        parentBlocKid = blockId;
                    }
                    else
                    {
                        // Add a child block to the parent block.
                        var blockid = blocks.Count;
                        var block = blocks[parentBlocKid];

                        block.Nodes.Add(prefix, blockid);

                        // The parent's block width must accomodate the prefix of the child.
                        // This is always max_bits_per_block bits.
                        block.Width = Math.Max(block.Width, _maxBitsPerBlock);

                        // Add the child block.
                        blocks.Add(new CodebookBlock<TValueType>());

                        parentBlocKid = blockid;
                    }
                }

                // The final chunk of code bits always has <= max_bits_per_block bits. Obtain
                // the final prefix.
                //                    let prefix = code & (prefix_mask >> (self.max_bits_per_block - len));
                var finalPrefix = (ushort)(code & (prefixMask >> (_maxBitsPerBlock - len)));

                var finalBlock = blocks[parentBlocKid];

                // Add the value to the block.
                finalBlock.Values.Add(new CodebookValue<TValueType>
                {
                    Prefix = finalPrefix,
                    Width = len,
                    Value = value
                });

                finalBlock.Width = Math.Max(finalBlock.Width, len);

                maxCodeLen = Math.Max(maxCodeLen, codeLen);
            }
        }

        var table = GenerateLut<TValueType, TOffsetType>(_bitOrder, _isSparse, blocks);
        var initBlockLen = table.Any() ? table[0].JumpLen : 0;

        return new Codebook<TValueType, TOffsetType>
        {
            Table = table,
            MaxCodeLength = Math.Max(maxCodeLen, codeLens.Max()),
            InitBlockLength = initBlockLen
        };
    }

    private ICodebookEntry<TValueType, TOffsetType>[]
        GenerateLut<TValueType, TOffsetType>
        (BitOrder bitOrder, bool isSparse, List<CodebookBlock<TValueType>> blocks)
        where TOffsetType : unmanaged where TValueType : unmanaged
    {
        // The codebook table.
        var table = new List<ICodebookEntry<TValueType, TOffsetType>>();

        var queue = new Queue<int>();

        uint tableEnd = 0;

        if (blocks.Any())
        {
            queue.Enqueue(0);

            // The first entry in the table is always a jump to the first block.
            var block = blocks[0];
            var one = (uint)1;
            table.Add(CodebookEntryExt.NewJump<TValueType, TOffsetType>(Unsafe.As<uint, TValueType>(ref one),
                block.Width));
            tableEnd += (uint)(1 << block.Width) + 1;
        }

        //Traverse the tree in breadth-first order.
        while (queue.Any())
        {
            //count the number of nodes in the current level
            var entryCount = 0;

            //get the block id at the front of the queue
            var blockId = queue.Dequeue();

            //get the block
            var block = blocks[blockId];
            var blockLen = 1 << block.Width;

            //Starting index
            var tableBase = table.Count;

            //Resize the table to fit the current block
            table.AddRange(Enumerable.Repeat(CodebookEntryExt.Default<TValueType, TOffsetType>(), blockLen));

            // Push child blocks onto the queue and record the jump entries in the table. Jumps
            // will be in order of increasing prefix because of the implicit sorting provided
            // by BTreeMap, thus traversing a level of the tree left-to-right.
            foreach (var (prefix, childBlockId) in block.Nodes)
            {
                // Push the child block onto the queue.
                queue.Enqueue(childBlockId);

                // The width of the child block in bits.
                var childBlockWidth = blocks[childBlockId].Width;

                //VErify that the child block is not too large to fit in the current block.
                if (tableEnd > Entry32x32.JumpOffestMax)
                    throw new NotSupportedException("Child block is too large to fit in the current block.");


                // Determine the offset into the table depending on the bit-order.
                /*     // Determine the offset into the table depending on the bit-order.
                    let offset = match bit_order {
                        BitOrder::Verbatim => child_block_prefix,
                        BitOrder::Reverse => {
                            child_block_prefix.reverse_bits().rotate_left(u32::from(block.width))
                        }
                    } as usize;*/
                var offset = bitOrder switch
                {
                    BitOrder.Reverse => prefix.ReverseBits().RotateLeft((int)block.Width),
                    BitOrder.Verbatim => prefix
                };

                // Add a jump entry to table.
                var tableEndCast = Unsafe.As<uint, TValueType>(ref tableEnd);
                var jumpEntry = CodebookEntryExt.NewJump<TValueType, TOffsetType>(tableEndCast, childBlockWidth);
                table[(int)offset + tableBase] = jumpEntry;

                // Increment the table end.
                tableEnd += (uint)(1 << childBlockWidth);

                // Increment the entry count.
                entryCount += 1;
            }

            // Add value entries into the table. If a value has a prefix width less than the
            // block width, then do-not-care bits must added to the end of the prefix to pad it
            // to the block width.
            foreach (var value in block.Values)
            {
                //the number of do-not-care bits to add to the end of the prefix
                var numDncBits = block.Width - value.Width;

                //Extend the prefix to the block width
                var basePrefix = value.Prefix << (int)numDncBits;

                //using the bit-order, generate the prefix for each possible value of the do-not-care bits
                var count = 1 << (int)numDncBits;

                //the value to add to the table
                var valueVal = value.Value;
                var valueEntry =
                    CodebookEntryExt.NewValue<TValueType, TOffsetType>(valueVal,
                        value.Width);

                switch (bitOrder)
                {
                    case BitOrder.Verbatim:
                    {
                        var start = (uint)basePrefix + (uint)tableBase;
                        var end = start + count;

                        for (uint offset = start; offset < end; offset++)
                        {
                            table[(int)offset] = valueEntry;
                        }

                        break;
                    }
                    case BitOrder.Reverse:
                    {
                        //for reverse bit order, the do-not-care bits are at the beginning of the prefix
                        var start = (uint)basePrefix;
                        var end = start + count;

                        for (uint prefix = start; prefix < end; prefix++)
                        {
                            var offset = prefix.ReverseBits().RotateLeft((int)block.Width);
                            table[(int)offset + tableBase] = valueEntry;
                        }

                        break;
                    }
                }

                // Update the entry count.
                entryCount += count;
            }

            // If the decoding tree is not sparse, the number of entries added to the table
            // should equal the block length if the. It is a fatal error if this is not true.
            if (!isSparse && entryCount != blockLen)
                throw new NotSupportedException(
                    "The decoding tree is not sparse and the number of entries added to the table should equal the block length if the.");
        }

        return table.ToArray();
    }
}

public enum BitOrder
{
    Reverse,
    Verbatim
}