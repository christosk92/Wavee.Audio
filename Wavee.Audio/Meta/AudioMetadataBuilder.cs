using Wavee.Audio.Meta.Metadata;

namespace Wavee.Audio.Meta;

public record AudioMetadataBuilder
{
    public AudioMetadataBuilder()
    {
        Metadata = new MetadataRevision(new List<Tag>(), new List<Visual>(), new List<VendorData>());
    }

    public MetadataRevision Metadata { get; init; }
}