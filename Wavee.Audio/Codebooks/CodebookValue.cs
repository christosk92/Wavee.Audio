namespace Wavee.Audio.Codebooks;

internal class CodebookValue<TValueType> where TValueType : unmanaged
{
    public ushort Prefix { get; init; }
    public byte Width { get; init; }
    public TValueType Value { get; init; }
}