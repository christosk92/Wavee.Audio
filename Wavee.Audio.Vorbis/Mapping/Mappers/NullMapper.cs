using Wavee.Audio.Codecs;

namespace Wavee.Audio.Vorbis.Mapping.Mappers;

internal class NullMapper : IMapper
{
    public string Name => "null";
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