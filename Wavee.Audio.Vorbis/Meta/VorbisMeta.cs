using System.Text;
using Wavee.Audio.Helpers.Extensions;
using Wavee.Audio.IO;
using Wavee.Audio.Meta;
using Wavee.Audio.Meta.Metadata;
using Wavee.Audio.Meta.TagVal;

namespace Wavee.Audio.Vorbis.Meta;

public static class VorbisMeta
{
    private static readonly Dictionary<string, StandardTagKey> Map = new Dictionary<string, StandardTagKey>
    {
        { "album artist", StandardTagKey.AlbumArtist },
        { "album", StandardTagKey.Album },
        { "albumartist", StandardTagKey.AlbumArtist },
        { "albumartistsort", StandardTagKey.SortAlbumArtist },
        { "albumsort", StandardTagKey.SortAlbum },
        { "arranger", StandardTagKey.Arranger },
        { "artist", StandardTagKey.Artist },
        { "artistsort", StandardTagKey.SortArtist },
        // TODO: Is Author a synonym for Writer?
        { "author", StandardTagKey.Writer },
        { "barcode", StandardTagKey.IdentBarcode },
        { "bpm", StandardTagKey.Bpm },
        { "catalog #", StandardTagKey.IdentCatalogNumber },
        { "catalog", StandardTagKey.IdentCatalogNumber },
        { "catalognumber", StandardTagKey.IdentCatalogNumber },
        { "catalogue #", StandardTagKey.IdentCatalogNumber },
        { "comment", StandardTagKey.Comment },
        { "compileation", StandardTagKey.Compilation },
        { "composer", StandardTagKey.Composer },
        { "conductor", StandardTagKey.Conductor },
        { "copyright", StandardTagKey.Copyright },
        { "date", StandardTagKey.Date },
        { "description", StandardTagKey.Description },
        { "disc", StandardTagKey.DiscNumber },
        { "discnumber", StandardTagKey.DiscNumber },
        { "discsubtitle", StandardTagKey.DiscSubtitle },
        { "disctotal", StandardTagKey.DiscTotal },
        { "disk", StandardTagKey.DiscNumber },
        { "disknumber", StandardTagKey.DiscNumber },
        { "disksubtitle", StandardTagKey.DiscSubtitle },
        { "disktotal", StandardTagKey.DiscTotal },
        { "djmixer", StandardTagKey.MixDj },
        { "ean/upn", StandardTagKey.IdentEanUpn },
        { "encoded-by", StandardTagKey.EncodedBy },
        { "encoder settings", StandardTagKey.EncoderSettings },
        { "encoder", StandardTagKey.Encoder },
        { "encoding", StandardTagKey.EncoderSettings },
        { "engineer", StandardTagKey.Engineer },
        { "ensemble", StandardTagKey.Ensemble },
        { "genre", StandardTagKey.Genre },
        { "isrc", StandardTagKey.IdentIsrc },
        { "language", StandardTagKey.Language },
        { "label", StandardTagKey.Label },
        { "license", StandardTagKey.License },
        { "lyricist", StandardTagKey.Lyricist },
        { "lyrics", StandardTagKey.Lyrics },
        { "media", StandardTagKey.MediaFormat },
        { "mixer", StandardTagKey.MixEngineer },
        { "mood", StandardTagKey.Mood },
        { "musicbrainz_albumartistid", StandardTagKey.MusicBrainzAlbumArtistId },
        { "musicbrainz_albumid", StandardTagKey.MusicBrainzAlbumId },
        { "musicbrainz_artistid", StandardTagKey.MusicBrainzArtistId },
        { "musicbrainz_discid", StandardTagKey.MusicBrainzDiscId },
        { "musicbrainz_originalalbumid", StandardTagKey.MusicBrainzOriginalAlbumId },
        { "musicbrainz_originalartistid", StandardTagKey.MusicBrainzOriginalArtistId },
        { "musicbrainz_recordingid", StandardTagKey.MusicBrainzRecordingId },
        { "musicbrainz_releasegroupid", StandardTagKey.MusicBrainzReleaseGroupId },
        { "musicbrainz_releasetrackid", StandardTagKey.MusicBrainzReleaseTrackId },
        { "musicbrainz_trackid", StandardTagKey.MusicBrainzTrackId },
        { "musicbrainz_workid", StandardTagKey.MusicBrainzWorkId },
        { "opus", StandardTagKey.Opus },
        { "organization", StandardTagKey.Label },
        { "originaldate", StandardTagKey.OriginalDate },
        { "part", StandardTagKey.Part },
        { "performer", StandardTagKey.Performer },
        { "producer", StandardTagKey.Producer },
        { "productnumber", StandardTagKey.IdentPn },
        // TODO: Is Publisher a synonym for Label?
        { "publisher", StandardTagKey.Label },
        { "rating", StandardTagKey.Rating },
        { "releasecountry", StandardTagKey.ReleaseCountry },
        { "remixer", StandardTagKey.Remixer },
        { "replaygain_album_gain", StandardTagKey.ReplayGainAlbumGain },
        { "replaygain_album_peak", StandardTagKey.ReplayGainAlbumPeak },
        { "replaygain_track_gain", StandardTagKey.ReplayGainTrackGain },
        { "replaygain_track_peak", StandardTagKey.ReplayGainTrackPeak },
        { "script", StandardTagKey.Script },
        { "subtitle", StandardTagKey.TrackSubtitle },
        { "title", StandardTagKey.TrackTitle },
        { "titlesort", StandardTagKey.SortTrackTitle },
        { "totaldiscs", StandardTagKey.DiscTotal },
        { "totaltracks", StandardTagKey.TrackTotal },
        { "tracknumber", StandardTagKey.TrackNumber },
        { "tracktotal", StandardTagKey.TrackTotal },
        { "unsyncedlyrics", StandardTagKey.Lyrics },
        { "upc", StandardTagKey.IdentUpc },
        { "version", StandardTagKey.Version },
        { "writer", StandardTagKey.Writer },
        { "year", StandardTagKey.Date },
    };

    public static void ReadCommentNoFraming<T>(
        T reader,
        ref AudioMetadataBuilder builder)
        where T : IReadBytes
    {
        //Read the vendor string length in bytes
        var vendorLength = reader.ReadUInt();

        // Ignore the vendor string.
        reader.IgnoreBytes((ulong)vendorLength);

        // Read the number of comments.
        var nComments = reader.ReadUInt();

        for (var i = 0; i < nComments; i++)
        {
            // Read the comment length in bytes.
            var commentLength = reader.ReadUInt();

            // Read the comment string.
            Span<byte> commentByte = new byte[commentLength];
            reader.ReadExact(commentByte);

            // Parse the comment string into a Tag and insert it into the parsed tag list.
            builder
                .Metadata
                .Tags
                .Add(Parse(Encoding.UTF8.GetString(commentByte)));
        }
    }

    private static Tag Parse(string getString)
    {
        // Vorbis Comments (aka tags) are stored as <key>=<value> where <key> is
        // a reduced ASCII-only identifier and <value> is a UTF8 value.
        //
        // <Key> must only contain ASCII 0x20 through 0x7D, with 0x3D ('=') excluded.
        // ASCII 0x41 through 0x5A inclusive (A-Z) is to be considered equivalent to
        // ASCII 0x61 through 0x7A inclusive (a-z) for tag matching.

        var field = getString.Split('=', 2);

        var key = Map.TryGetValue(field[0].ToLower(), out var stdTag) ? stdTag : StandardTagKey.Unknown;
        // The value field was empty so only the key field exists. Create an empty tag for the given
        // key field.
        if (field.Length == 1)
        {
            return new Tag(key, field[0], new StringTagValue(string.Empty));
        }

        return new Tag(key, field[0], new StringTagValue(field[1]));
    }
}