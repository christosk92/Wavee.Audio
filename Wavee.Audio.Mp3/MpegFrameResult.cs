namespace Wavee.Audio.Mp3;

internal readonly ref struct MpegFrameResult
{
    public ReadOnlySpan<byte> Packet { get; init; }
    public FrameHeader Header { get; init; }

    //deconstruct
    public void Deconstruct(out FrameHeader header, out ReadOnlySpan<byte> packet)
    {
        header = Header;
        packet = Packet;
    }
}