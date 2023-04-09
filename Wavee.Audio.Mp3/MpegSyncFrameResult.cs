namespace Wavee.Audio.Mp3;

internal readonly ref struct MpegSyncFrameResult
{
    public uint HeaderWord { get; init; }
    public FrameHeader Header { get; init; }

    //deconstruct
    public void Deconstruct(out FrameHeader header, out uint headerWord)
    {
        header = Header;
        headerWord = HeaderWord;
    }
}