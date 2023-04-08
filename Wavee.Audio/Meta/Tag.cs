using Wavee.Audio.Meta.Metadata;
using Wavee.Audio.Meta.TagVal;

namespace Wavee.Audio.Meta;

/// <summary>
/// A <see cref="Tag"/> encapsulates a key-value pair of metadata.
/// </summary>
public record Tag(
    StandardTagKey StdKey,
    string Key,
    ITagValue Value
);