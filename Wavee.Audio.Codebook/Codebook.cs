namespace Wavee.Audio.Codebook;

public sealed class Codebook<TValueType, TOffsetType>
    where TValueType : unmanaged
    where TOffsetType : unmanaged
{
    public ICodebookEntry<TValueType, TOffsetType>[] Table { get; init; }
    public uint MaxCodeLength { get; init; }
    public uint InitBlockLength { get; init; }
}