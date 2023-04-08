using Wavee.Audio.Codecs;

namespace Wavee.Audio.Vorbis.Mapping.Mappers;

internal sealed class OpusMapper : IMapper
{
    public static bool TryDetect(ReadOnlySpan<byte> pkt, out OpusMapper mapper)
    {
        mapper = null;
        return false;
    }

    public string Name => "Opus";
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
}