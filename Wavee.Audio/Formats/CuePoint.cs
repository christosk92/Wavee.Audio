using Wavee.Audio.Meta;

namespace Wavee.Audio.Formats;

/// <summary>
/// A <see cref="CuePoint"/> is a point, represented as a frame offset, within a <see cref="Cue"/>.
///
/// A <see cref="CuePoint"/> provides more precise indexing within a parent <see cref="Cue"/>.
/// Additional <see cref="Tags"/> may be
/// associated with a <see cref="CuePoint"/>.
/// </summary>
/// <param name="StartOffsetTs">The offset of the first frame in the <see cref="CuePoint"/> relative to the start of the parent <see cref="Cue"/></param>
/// <param name="Tags">A list of <see cref="Tag"/>s associated with the <see cref="CuePoint"/>.</param>
public record CuePoint(
    ulong StartOffsetTs,
    Tag[] Tags
);