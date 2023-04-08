using Wavee.Audio.Meta.Metadata;

namespace Wavee.Audio.Vorbis.Mapping;

public interface ISideData
{
    public record MetadataSideData(MetadataRevision Metadata) : ISideData;
}