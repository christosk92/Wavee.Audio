using System.Diagnostics;

namespace Wavee.Audio.Vorbis.Decoding.Codebooks;

public record struct CodebookEntry(uint Value, uint Offset)
{
    public const uint JUMP_OFFEST_MAX = 0x7fff_ffff;
    private const uint JumpFlag = 0x8000_0000;

    public static CodebookEntry NewJump(uint offset, byte len)
    {
        var off = JumpFlag | offset;
        if (off == 2147484489)
            Debugger.Break();
        return new CodebookEntry(len, off);
    }
    public static CodebookEntry NewValue(uint value, byte len)
    {
        return new CodebookEntry(value, len);
    }
    
    public bool IsJump()
    {
        return (Offset & JumpFlag) != 0;
    }

    public bool IsValue()
    {
        return (Offset & JumpFlag) == 0;
    }
    
    public int JumpOffset()
    {
        Debug.Assert(IsJump());
        return (int)(Offset & ~JumpFlag);
    }

    public uint JumpLen()
    {
        Debug.Assert(IsJump());
        return Value;
    }

    public uint ValueLen()
    {
        return Offset & ~JumpFlag;
    }
}