using System.Diagnostics;
using Wavee.Audio.IO;
using Wavee.Audio.Vorbis.Exception;
using Wavee.Audio.Vorbis.Mapping.Mappers;

namespace Wavee.Audio.Vorbis;

internal static class Utils
{
    internal static uint BitReverse(uint n, int bits)
    {
        n = ((n & 0xAAAAAAAA) >> 1) | ((n & 0x55555555) << 1);
        n = ((n & 0xCCCCCCCC) >> 2) | ((n & 0x33333333) << 2);
        n = ((n & 0xF0F0F0F0) >> 4) | ((n & 0x0F0F0F0F) << 4);
        n = ((n & 0xFF00FF00) >> 8) | ((n & 0x00FF00FF) << 8);
        return ((n >> 16) | (n << 16)) >> (32 - bits);
    }

    public static uint ILog(this uint x)
    {
        uint cnt = 0;
        while (x > 0)
        {
            ++cnt;
            x >>= 1; // this is safe because we'll never get here if the sign bit is set
        }

        return cnt;
    }

    public static uint lookup1_values(uint entries, ushort dimensions)
    {
        var r = (uint)Math.Floor(Math.Exp(Math.Log(entries) / dimensions));

        if (Math.Floor(Math.Pow(r + 1, dimensions)) <= entries) ++r;

        return r;
    }

    public static Mode[] ReadModes(BitReaderRtl bs)
    {
        static Mode ReadMode(BitReaderRtl bs)
        {
            var blockFlag = bs.ReadBool();
            var windowType = (ushort)bs.ReadBitsLeq32(16);
            var transformType = (ushort)bs.ReadBitsLeq32(16);
            var _mapping = (byte)bs.ReadBitsLeq32(8);

            // Only window type 0 is allowed in Vorbis 1 (section 4.2.4).
            if (windowType != 0)
            {
                Debug.WriteLine("Vorbis mode window type is invalid.");
                throw new OggDecodeException("Vorbis mode window type is invalid.");
                //   return default;
            }

            // Only transform type 0 is allowed in Vorbis 1 (section 4.2.4).
            if (transformType != 0)
            {
                Debug.WriteLine("Vorbis mode transform type is invalid.");
                throw new OggDecodeException("Vorbis mode transform type is invalid.");
                //   return default;
            }

            return new Mode(blockFlag, _mapping);
        }

        var count = (byte)bs.ReadBitsLeq32(6) + 1;
        var modes = new Mode[count];
        for (int i = 0; i < count; i++)
        {
            modes[i] = ReadMode(bs);
        }

        return modes;
    }

    public static float Float32Unpack(this uint x)
    {
        uint mantissa = x & 0x1fffff;
        uint sign = x & 0x80000000;
        int exponent = (int)((x & 0x7fe00000) >> 21);
        float value = mantissa * (float)Math.Pow(2.0, exponent - 788);

        if (sign == 0)
        {
            return value;
        }
        else
        {
            return -value;
        }
    }
}