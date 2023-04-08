using System.Diagnostics;
using Wavee.Audio.IO;
using Wavee.Audio.Vorbis.Logical;
using Wavee.Audio.Vorbis.Mapping.Mappers;
using Wavee.Audio.Vorbis.Pages;

namespace Wavee.Audio.Vorbis.Physical;

internal static class PhysicalStream
{
    public static void ProbeStreamStart(
        MediaSourceStream reader,
        OggPageReader pages,
        SortedDictionary<uint, OggLogicalStream> streams)
    {
        // Save the original position to jump back to.
        var originalPos = reader.Pos();

        // Scope the reader the prevent overruning the seekback region.
        var scopedReader = new ScopedStream<MediaSourceStream>(
            inner: reader,
            len: (ulong)OggPageHeader.OGG_PAGE_MAX_SIZE
        );

        var probed = new SortedSet<uint>();

        // Examine the first bitstream page of each logical stream within the physical stream to
        while (true)
        {
            var page = pages.Page();

            // If the page does not belong to the current physical stream, break out.
            if (!streams.TryGetValue(page.Header.Serial, out var stream))
            {
                break;
            }

            // If the stream hasn't been marked as probed.
            if (!probed.Contains(page.Header.Serial))
            {
                // Probe the stream.
                stream.InspectStartPage(page);

                // Mark the stream as probed.
                probed.Add(page.Header.Serial);
            }

            // If all logical streams were probed, break out immediately.
            if (probed.Count >= streams.Count)
                break;

            // Read the next page.
            try
            {
                pages.TryNextPage(scopedReader);
            }
            catch (System.Exception e)
            {
                Debug.WriteLine(e);
            }
        }

        scopedReader.Inner.SeekBuffered(originalPos);
    }

    public static ulong? ProbeStreamEnd(MediaSourceStream reader,
        OggPageReader pages,
        SortedDictionary<uint, OggLogicalStream> streams,
        ulong byteRangeStart,
        ulong byteRangeEnd)
    {
        // Save the original position.
        var originalPos = reader.Pos();

        // Number of bytes to linearly scan. We assume the OGG maximum page size for each logical
        // stream.
        var linearScalLen = (ulong)OggPageHeader.OGG_PAGE_MAX_SIZE * (ulong)streams.Count;

        // Optimization: Try a linear scan of the last few pages first. This will cover all
        // non-chained physical streams, which is the majority of cases.
        if (byteRangeEnd >= linearScalLen && byteRangeStart <= byteRangeEnd - linearScalLen)
        {
            reader.Seek(SeekOrigin.Begin, byteRangeEnd - linearScalLen);
        }
        else
        {
            reader.Seek(SeekOrigin.Begin, byteRangeStart);
        }

        pages.NextPage(reader);

        var result = ScanStreamEnd(reader, pages, streams, byteRangeEnd);

        // If there are no pages belonging to the current physical stream at the end of the media
        // source stream, then one or more physical streams are chained. Use a bisection method to find
        // the end of the current physical stream.
        if (result is null)
        {
            Debug.WriteLine("Chained physical stream detected. Bisection search for end of stream.");

            var start = byteRangeStart;
            var end = byteRangeEnd;

            //TODO:
        }

        reader.Seek(SeekOrigin.Begin, originalPos);

        return result.Value;
    }

    private static ulong? ScanStreamEnd(MediaSourceStream reader, OggPageReader pages,
        SortedDictionary<uint, OggLogicalStream> streams, ulong byteRangeEnd)
    {
        var scopedLen = byteRangeEnd - reader.Pos();

        var scopedReader = new ScopedStream<MediaSourceStream>(reader, scopedLen);

        ulong? upperPos = null;

        var state = new InspectState();

        // Read pages until the provided end position or a new physical stream starts.
        while (true)
        {
            var page = pages.Page();

            // If the page does not belong to the current physical stream, then break out, the
            // extent of the physical stream has been found.
            if (!streams.TryGetValue(page.Header.Serial, out var stream))
            {
                break;
            }

            state = stream.InspectEndPage(state, page);

            // The new end of the physical stream is the position after this page.
            upperPos = reader.Pos();

            // Read to the next page.
            try
            {
                pages.NextPage(scopedReader);
            }
            catch (System.Exception e)
            {
                Debug.WriteLine(e);
                break;
            }
        }

        return upperPos;
    }
}

internal record InspectState
{
    public VorbisPacketParser? Parser { get; set; }
    public Bound? Bound { get; set; }
}