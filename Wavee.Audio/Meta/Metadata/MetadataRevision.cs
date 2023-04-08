namespace Wavee.Audio.Meta.Metadata;

/// <summary>
/// <see cref="MetadataRevision"/> is a container for a single discrete revision of metadata information.
/// </summary>
public record MetadataRevision(
    List<Tag> Tags,
    List<Visual> Visuals,
    List<VendorData> VendorData);