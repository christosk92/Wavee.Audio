using System.Diagnostics;
using Wavee.Audio.IO;
using Wavee.Audio.Vorbis.Exception;

namespace Wavee.Audio.Vorbis.Mapping.Mappers;

internal class VorbisPacketParser : IPacketParser
{
    internal readonly ulong ModesBlockFlags;
    internal readonly byte NumModes;
    internal readonly byte Bs0Exp;
    internal readonly byte Bs1Exp;
    private byte? _prevBsEx;

    public VorbisPacketParser(byte identHeaderBs0Exp, byte identHeaderBs1Exp, byte numModes, ulong modesBlockFlags)
    {
        ModesBlockFlags = (ulong)modesBlockFlags;
        NumModes = numModes;
        Bs0Exp = identHeaderBs0Exp;
        Bs1Exp = identHeaderBs1Exp;
    }

    public ulong ParseNextPacketDur(ReadOnlySpan<byte> data)
    {
        var bs = new BitReaderRtl(data.ToArray());

        // First bit must be 0 to indicate audio packet.
        var bit = bs.ReadBool();
        if (bit)
        {
            return 0;
        }

        // Number of bits for the mode number.
        var modenumBits = ((uint)NumModes - 1).ILog();

        // Read the mode number.
        byte modeNum = 0;
        try
        {
            modeNum = (byte)bs.ReadBitsLeq32(modenumBits);
        }
        catch (System.Exception e)
        {
            Debug.WriteLine("Vorbis packet mode number is invalid. Setting to 0.");
            return 0;
        }

        // Determine the current block size.
        byte curBsExp;
        if (modeNum < NumModes)
        {
            var blockFlag = (ModesBlockFlags >> modeNum) & 1;
            if (blockFlag == 1)
            {
                curBsExp = Bs1Exp;
            }
            else
            {
                curBsExp = Bs0Exp;
            }
        }
        else
        {
            return 0;
        }

        ulong dur;
        if (_prevBsEx.HasValue)
        {
            dur = ((ulong)1 << _prevBsEx.Value) >> 2;
            dur += ((ulong)1 << curBsExp) >> 2;
        }
        else
        {
            dur = 0;
        }

        _prevBsEx = curBsExp;
        return dur;
    }

    public void Reset()
    {
        _prevBsEx = null;
    }
}