namespace Wavee.Audio.Vorbis.Decoding.Codebooks;

public class Codebook
{
    public CodebookEntry[] Table { get; init; }
    public uint MaxCodeLength { get; init; }
    public uint InitBlockLength { get; init; }
}