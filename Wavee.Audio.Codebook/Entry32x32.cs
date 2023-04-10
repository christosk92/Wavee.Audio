using System.Diagnostics;

namespace Wavee.Audio.Codebook;

public record Entry32x32(uint Value, uint Offset) :
    ICodebookEntry<uint, uint>
{
    public const uint JumpOffestMax = 0x7fff_ffff;
    public const uint JumpFlag = 0x8000_0000;

    public uint JumpOffsetMax => JumpOffestMax;

    public Entry32x32() : this(0, 0)
    {
        
    }

    public bool IsJump => (Offset & JumpFlag) != 0;

    public bool IsValue => (Offset & JumpFlag) == 0;

    public uint JumpOffset
    {
        get
        {
            Debug.Assert(IsJump);
            return (Offset & ~JumpFlag);
        }
    }

    public uint JumpLen
    {
        get
        {
            Debug.Assert(IsJump);
            return Value;
        }
    }

    public uint ValueLen => Offset & ~JumpFlag;
}