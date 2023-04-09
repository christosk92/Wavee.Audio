using System.Buffers.Binary;
using System.Diagnostics;
using System.Drawing;
using Wavee.Audio.Codebooks;

namespace Wavee.Audio.IO;

/// <summary>
/// Reads bits from least-significant to most-significant.
/// </summary>
public sealed class BitReaderRtl
{
    private Memory<byte> _buf;
    private ulong _bits;
    private uint _nBitsLeft;

    public BitReaderRtl(Memory<byte> buf)
    {
        _buf = buf;
        _bits = 0;
        _nBitsLeft = 0;
    }

    /// <summary>
    /// Reads and returns up to 32-bits or returns an error.
    /// </summary>
    /// <param name="i"></param>
    /// <returns></returns>
    public int ReadBitsLeq32(uint bitWidth)
    {
        Debug.Assert(bitWidth <= 32);

        var bits = _bits;
        var bitsNeeded = bitWidth;

        while (bitsNeeded > _nBitsLeft)
        {
            bitsNeeded -= _nBitsLeft;

            FetchBits();

            bits |= _bits << (int)(bitWidth - bitsNeeded);
        }

        ConsumeBits(bitsNeeded);

        // Since bitWidth is <= 32, this shift will never throw an exception.
        uint mask = bitWidth == 32 ? ~0U : ~(~0U << (int)bitWidth);
        return (int)(bits & mask);
    }

    private void ConsumeBits(uint num)
    {
        _nBitsLeft -= num;
        _bits >>= (int)num;
    }

    private void FetchBits()
    {
        Span<byte> buf = stackalloc byte[sizeof(ulong)];

        var readLen = Math.Min(_buf.Length, sizeof(ulong));

        if (readLen == 0)
        {
            throw new EndOfStreamException();
        }

        _buf.Span[..readLen].CopyTo(buf);

        _buf = _buf[readLen..];

        _bits = BinaryPrimitives.ReadUInt64LittleEndian(buf);
        _nBitsLeft = (uint)readLen << 3;
    }

    public bool ReadBool()
    {
        if (_nBitsLeft < 1)
        {
            FetchBits();
        }

        var bit = (_bits & 1) == 1;
        ConsumeBits(1);
        return bit;
    }

    public void IgnoreBits(uint numBits)
    {
        if (numBits <= _nBitsLeft)
        {
            ConsumeBits(numBits);
        }
        else
        {
            // Consume whole bit caches directly.
            while (numBits > _nBitsLeft)
            {
                numBits -= _nBitsLeft;
                FetchBits();
            }

            if (numBits > 0)
            {
                // Shift out in two parts to prevent panicing when num_bits == 64.
                ConsumeBits(numBits - 1);
                ConsumeBits(1);
            }
        }
    }

    public (uint, uint) ReadCodebook(Codebook codebook)
    {
        if (_nBitsLeft < codebook.MaxCodeLength)
        {
            FetchBitsPartial();
        }

        // The number of bits actually buffered in the bit buffer.
        var numBitsLeft = _nBitsLeft;

        var bits = _bits;

        var blockLen = codebook.InitBlockLength;
        //        let mut entry = codebook.table[(bits & ((1 << block_len) - 1)) as usize + 1];
        int result = (int)((bits & ((1UL << (int)blockLen) - 1)) + 1);
        var entry = codebook.Table[result];

        uint consumed = 0;

        while (entry.IsJump())
        {
            // Consume the bits used for the initial or previous jump iteration.
            consumed += blockLen;
            bits >>= (int)blockLen;

            // Since this is a jump entry, if there are no bits left then the bitstream ended early.
            if (consumed > numBitsLeft)
            {
                throw new EndOfStreamException();
            }

            //prepare for next jump
            blockLen = entry.JumpLen();
            // ulong index = bits >> (64 - (int)blockLen);
            //
            // let index = bits & ((1 << block_len) - 1);
            ulong index = bits & (((ulong)1 << (int)blockLen) - 1);

            // Jump to the next entry.
            var jmp = entry.JumpOffset();
            entry = codebook.Table[jmp + (int)index];
        }

        // The entry is always a value entry at this point. Consume the bits containing the value.
        consumed += entry.ValueLen();

        if (consumed > numBitsLeft)
        {
            throw new EndOfStreamException();
        }

        ConsumeBits(consumed);
        return (entry.Value, consumed);
    }

    private void FetchBitsPartial()
    {
        Span<byte> buf = stackalloc byte[sizeof(ulong)];

        var readLen = Math.Min(_buf.Length, (int)(64 - _nBitsLeft) >> 3);
        _buf.Span[..readLen].CopyTo(buf[..readLen]);

        _buf = _buf[readLen..];

        _bits |= BinaryPrimitives.ReadUInt64LittleEndian(buf) << (int)_nBitsLeft;
        _nBitsLeft += (uint)readLen << 3;
    }
}