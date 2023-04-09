using Wavee.Audio.Meta;

namespace Wavee.Audio.Formats;

/// <summary>
/// A <see cref="Cue"/> is a designated point of time within a media stream.
///
/// A <see cref="Cue"/> may be a mapping from either a source track, a chapter, cuesheet, or a timestamp
/// depending on the source media. A `Cue`'s duration is the difference between the `Cue`'s
/// timestamp and the next. Each `Cue` may contain an optional index of points relative to the `Cue`
/// that never exceed the timestamp of the next `Cue`. A `Cue` may also have associated `Tag`s.
/// </summary>
/// <param name="Index"> A unique index for the <see cref="Cue"/>.</param>
/// <param name="StartTs">The starting timestamp in number of frames from the start of the stream.</param>
/// <param name="Tags"> A list of <see cref="Tag"/>s associated with the <see cref="Cue"/>.</param>
/// <param name="Points">
/// A list of <see cref="CuePoint"/>s that are contained within this <see cref="Cue"/>. These points are children of
/// the <see cref="Cue"/> since the <see cref="Cue"/> itself is an implicit <see cref="CuePoint"/>.
/// </param>
public record Cue(
    uint Index,
    ulong StartTs,
    Tag[] Tags,
    CuePoint[] Points
);