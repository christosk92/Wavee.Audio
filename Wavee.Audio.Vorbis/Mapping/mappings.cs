using Wavee.Audio.Vorbis.Mapping.Mappers;

namespace Wavee.Audio.Vorbis.Mapping;

internal static class mappings
{
    /// <summary>
    /// Detect a <see cref="IMapper"/> for a logical stream given the identification packet of the stream.
    /// </summary>
    /// <param name="pkt"></param>
    /// <returns></returns>
    public static IMapper Detect(ReadOnlySpan<byte> pkt)
    {
        if (FlacMapper.TryDetect(pkt, out var flacMapper))
            return flacMapper;
        
        if(VorbisMapper.TryDetect(pkt, out var vorbisMapper))
            return vorbisMapper;
        
        if (OpusMapper.TryDetect(pkt, out var opusMapper))
            return opusMapper;

        return MakeNullMapper();
    }
    
    private static IMapper MakeNullMapper()
    {
        return new NullMapper();
    }
}