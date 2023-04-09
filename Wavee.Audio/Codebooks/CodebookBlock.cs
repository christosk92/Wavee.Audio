namespace Wavee.Audio.Codebooks;

internal class CodebookBlock
{
    public CodebookBlock()
    {
        Width = 0;
        Nodes = new SortedDictionary<ushort, int>();
        Values = new List<CodebookValue>();
    }
    public byte Width { get; set; }
    public SortedDictionary<ushort, int> Nodes { get; init; }
    public List<CodebookValue> Values { get; init; }
}