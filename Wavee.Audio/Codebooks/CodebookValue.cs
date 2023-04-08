namespace Wavee.Audio.Vorbis.Decoding.Codebooks;

internal class CodebookValue
{
    public ushort Prefix { get; init; }
    public byte Width { get; init; }
    public uint Value { get; init; }
}