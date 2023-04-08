namespace Wavee.Audio.Vorbis.Pages;

internal class OggPage
{
    private List<ushort> _packetLens;
    private byte[] _bytes;

    public OggPage(OggPageHeader header, List<ushort> packetLens, byte[] bytes)
    {
        Header = header;
        _packetLens = packetLens;
        _bytes = bytes;
    }

    public OggPageHeader Header { get; }
    public int NumPacket => _packetLens.Count;

    public PagePackets Packets() => new PagePackets(_packetLens, _bytes);
}