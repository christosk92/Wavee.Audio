using System.Buffers.Binary;
using System.Diagnostics;

namespace Wavee.Audio.IO;

public sealed class BitReaderLtr
{
    private Memory<byte> _buf;
    private ulong _bits;
    private uint _nBitsLeft;

    public BitReaderLtr(Memory<byte> buf)
    {
        _buf = buf;
        _bits = 0;
        _nBitsLeft = 0;
    }

    public uint ReadBitsLeq32(uint bitWidth)
    {
        const uint u32Bits = 32;
        const ulong u64Bits = 64;

        Debug.Assert(bitWidth <= 32);

        // Shift in two 32-bit operations instead of a single 64-bit operation to avoid panicing
        // when bit_width == 0 (and thus shifting right 64-bits). This is preferred to branching
        // the bit_width == 0 case, since reading up-to 32-bits at a time is a hot code-path.
        ulong bits = (_bits >> (int)u32Bits) >> (int)(u32Bits - bitWidth);

        while (bitWidth > _nBitsLeft)
        {
            bitWidth -= _nBitsLeft;

            FetchBits();

            // Unlike the first shift, bitWidth is always > 0 here so this operation will never
            // shift by > 63 bits.
            bits |= _bits >> (int)(u64Bits - bitWidth);
        }

        ConsumeBits(bitWidth);

        return (uint)bits;
    }


    public void IgnoreBits(uint numBits)
    {
        if (numBits <= _nBitsLeft)
        {
            ConsumeBits(numBits);
        }
        else
        {
            while (numBits > _nBitsLeft)
            {
                numBits -= _nBitsLeft;
                FetchBits();
            }

            if (numBits > 0)
            {
                ConsumeBits(numBits - 1);
                ConsumeBits(1);
            }
        }
    }

    public bool ReadBool()
    {
        if (_nBitsLeft < 1)
            FetchBits();

        var bit = (_bits & (1UL << 63)) != 0;

        ConsumeBits(1);
        return bit;
    }

    public uint ReadBit()
    {
        if (_nBitsLeft < 1)
            FetchBits();

        var bit = _bits >> 63;
        ConsumeBits(1);
        return (uint)bit;
    }

    private void FetchBits()
    {
        Span<byte> buf = stackalloc byte[sizeof(ulong)];

        var readLen = Math.Min(_buf.Length, sizeof(ulong));

        if (readLen == 0)
        {
            throw new EndOfStreamException();
        }

        _buf[..(int)readLen].Span.CopyTo(buf[..readLen]);
        _buf = _buf[(int)readLen..];

        _bits = BinaryPrimitives.ReadUInt64BigEndian(buf);
        _nBitsLeft = (uint)readLen << 3;
    }

    private void ConsumeBits(uint num)
    {
        _nBitsLeft -= num;
        _bits <<= (int)num;
    }
}