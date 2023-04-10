namespace Wavee.Audio.Codebooks;

internal class CodebookBlock<TValueType> where TValueType : unmanaged
{
    public CodebookBlock()
    {
        Width = 0;
        Nodes = new SortedDictionary<ushort, int>();
        Values = new List<CodebookValue<TValueType>>();
    }

    public byte Width { get; set; }
    public SortedDictionary<ushort, int> Nodes { get; init; }
    public List<CodebookValue<TValueType>> Values { get; init; }
}