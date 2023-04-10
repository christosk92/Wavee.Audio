using System.Runtime.CompilerServices;

namespace Wavee.Audio.Codebook;

public interface ICodebookEntry<TValueType, TOffsetType>
    where TValueType : unmanaged
    where TOffsetType : unmanaged
{
    uint JumpOffsetMax { get; }

    bool IsValue { get; }

    bool IsJump { get; }

    TValueType Value { get; }

    uint ValueLen { get; }

    uint JumpOffset { get; }

    uint JumpLen { get; }
}

public static class CodebookEntryExt
{
    public static ICodebookEntry<TValueType, TOffsetType>?
        NewValue<TValueType, TOffsetType>(
            TValueType val,
            byte len)
        where TValueType : unmanaged
        where TOffsetType : unmanaged
    {
        //byte, byte -> 8x8
        //byte, ushort -> 8x16
        //byte, uint -> 8x32
        //ushort, byte -> 16x8
        //ushort, ushort -> 16x16
        //ushort, uint -> 16x32
        //uint, byte -> 32x8
        //uint, ushort -> 32x16
        //uint, uint -> 32x32

        return val switch
        {
            // byte when typeof(TOffsetType) == typeof(byte) =>
            //     new Entry8x8(value, len) as ICodebookEntry<TValueType, TOffsetType>,
            // byte when typeof(TOffsetType) == typeof(ushort) =>
            //     new Entry8x16(value, len) as ICodebookEntry<TValueType, TOffsetType>,
            // byte when typeof(TOffsetType) == typeof(uint) =>
            //     new Entry8x32(value, len) as ICodebookEntry<TValueType, TOffsetType>,
            // ushort when typeof(TOffsetType) == typeof(byte) =>
            //     new Entry16x8(value, len) as ICodebookEntry<TValueType, TOffsetType>,
            // ushort when typeof(TOffsetType) == typeof(ushort) =>
            //     new Entry16x16(value, len) as ICodebookEntry<TValueType, TOffsetType>,
            // ushort when typeof(TOffsetType) == typeof(uint) =>
            //     new Entry16x32(value, len) as ICodebookEntry<TValueType, TOffsetType>,
            // uint when typeof(TOffsetType) == typeof(byte) =>
            //     new Entry32x8(value, len) as ICodebookEntry<TValueType, TOffsetType>,
            // uint when typeof(TOffsetType) == typeof(ushort) =>
            //     new Entry32x16(value, len) as ICodebookEntry<TValueType, TOffsetType>,
            uint when typeof(TOffsetType) == typeof(uint) =>
                new Entry32x32(Unsafe.As<TValueType, uint>(ref val),
                    len) as ICodebookEntry<TValueType, TOffsetType>,
            _ => throw new NotImplementedException()
        };
    }

    public static ICodebookEntry<TValueType, TOffsetType>?
        NewJump<TValueType, TOffsetType>(
            TValueType val,
            byte len)
        where TValueType : unmanaged
        where TOffsetType : unmanaged
    {
        switch (val)
        {
            case uint when typeof(TOffsetType) == typeof(uint):
            {
                var off = Entry32x32.JumpFlag | Unsafe.As<TValueType, uint>(ref val);
                return new Entry32x32(len, off)
                    as ICodebookEntry<TValueType, TOffsetType>;
            }
        }

        throw new NotImplementedException();
    }

    public static ICodebookEntry<TValueType, TOffsetType> Default<TValueType, TOffsetType>()
        where TValueType : unmanaged where TOffsetType : unmanaged
    {
        if (typeof(TValueType) == typeof(uint) && typeof(TOffsetType) == typeof(uint))
        {
            return new Entry32x32(0, 0) as ICodebookEntry<TValueType, TOffsetType>;
        }

        throw new NotImplementedException();
    }
}