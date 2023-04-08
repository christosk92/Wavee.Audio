using Wavee.Audio.Codecs;

namespace Wavee.Audio.Vorbis.Mapping.Mappers;

internal interface IMapper
{
    string Name { get; }
    bool IsReady { get; }
    IMapResult MapPacket(ReadOnlySpan<byte> data);

    /// <summary>
    /// Convert an absolute granular position to a timestamp.
    /// </summary>
    /// <param name="ts"></param>
    /// <returns></returns>
    ulong AbsGpToTs(ulong ts) => ts;

    bool TryMakeParser(out VorbisPacketParser o);
    CodecParameters CodecParams();
    void UpdateCodecParams(CodecParameters o);
}

public interface IPacketParser
{
}