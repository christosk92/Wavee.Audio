using Wavee.Audio.Codecs;

namespace Wavee.Audio.Vorbis.Mapping.Mappers;

internal sealed class FlacMapper : IMapper
{
    public static bool TryDetect(ReadOnlySpan<byte> pkt, out FlacMapper mapper)
    {
        mapper = null;
        return false;
    }

    public string Name => "FLAC";
    public bool IsReady { get; }

    public IMapResult MapPacket(ReadOnlySpan<byte> data)
    {
        throw new NotImplementedException();
    }

    public bool TryMakeParser(out VorbisPacketParser o)
    {
        throw new NotImplementedException();
    }

    public CodecParameters CodecParams()
    {
        throw new NotImplementedException();
    }

    public void UpdateCodecParams(CodecParameters o)
    {
        throw new NotImplementedException();
    }

    public void Reset()
    {
        throw new NotImplementedException();
    }
}