using System.Diagnostics;
using Wavee.Audio.Formats;
using Wavee.Audio.IO;
using Wavee.Audio.Meta.Metadata;
using Wavee.Audio.Vorbis.Exception;
using Wavee.Audio.Vorbis.Logical;
using Wavee.Audio.Vorbis.Mapping;
using Wavee.Audio.Vorbis.Mapping.Mappers;
using Wavee.Audio.Vorbis.Pages;

namespace Wavee.Audio.Vorbis;

/// <summary>
/// OGG demultiplexer.
///
/// <see cref="OggReader"/> implements a demuxer for Xiph's OGG container format.
/// </summary>
public sealed class OggReader : IDisposable
{
    private readonly MediaSourceStream _reader;
    private List<Track> _tracks;
    private List<Cue> _cues;
    private MetadataLog _metadata;
    private readonly FormatOptions _options;

    /// <summary>
    /// The page reader.
    /// </summary>
    private OggPageReader _pages;

    /// <summary>
    /// `LogicalStream` for each serial.
    /// </summary>
    private SortedDictionary<uint, OggLogicalStream> _streams;

    /// <summary>
    /// The position of the first byte of the current physical stream.
    /// </summary>
    private ulong _physByteRangeStart;

    /// <summary>
    /// The position of the first byte of the next physical stream, if available.
    /// </summary>
    private ulong? _physByteRangeEnd;

    public OggReader(MediaSourceStream source, FormatOptions options)
    {
        // A seekback buffer equal to the maximum OGG page size is required for this reader.
        source.EnsureSeekbackBuffer(OggPageHeader.OGG_PAGE_MAX_SIZE);

        var pages = OggPageReader.TryNew(source);

        if (!pages.Header.IsFirstPage)
            throw new OggDecodeException(DecodeErrorType.PageNotFirst);

        _reader = source;
        _tracks = new List<Track>();
        _cues = new List<Cue>();
        _metadata = new MetadataLog(new Queue<MetadataRevision>());
        _streams = new SortedDictionary<uint, OggLogicalStream>();
        _options = options;
        _pages = pages;
        _physByteRangeStart = 0;
        _physByteRangeEnd = null;

        StartNewPhysicalStream();
    }

    public Track? DefaultTrack => _tracks.FirstOrDefault();

    private void StartNewPhysicalStream()
    {
        // The new mapper set.
        var streams = new SortedDictionary<uint, OggLogicalStream>();

        // The start of page position.
        var byteRangeStart = _reader.Pos();

        // Pre-condition: This function is only called when the current page is marked as a
        // first page.
        Debug.Assert(_pages.Header.IsFirstPage);

        Debug.WriteLine("Starting new physical stream");

        // The first page of each logical stream, marked with the first page flag, must contain the
        // identification packet for the encapsulated codec bitstream. The first page for each
        // logical stream from the current logical stream group must appear before any other pages.
        // That is to say, if there are N logical streams, then the first N pages must contain the
        // identification packets for each respective logical stream.
        while (true)
        {
            var header = _pages.Header;

            if (!header.IsFirstPage)
            {
                break;
            }

            byteRangeStart = _reader.Pos();

            // There should only be a single packet, the identification packet, in the first page.
            if (_pages.TryGetFirstPacket(out var pkt))
            {
                // If a stream mapper has been detected, create a logical stream with it.
                if (mappings.Detect(pkt) is { } mapper)
                {
                    Debug.WriteLine($"Selected {mapper.Name} mapper for stream with serial={header.Serial:x8}");

                    var stream = new OggLogicalStream(mapper, _options.EnableGapless);
                    streams[header.Serial] = stream;
                }
            }

            // Read the next page.
            _pages.TryNextPage(_reader);
        }

        // Each logical stream may contain additional header packets after the identification packet
        // that contains format-relevant information such as setup and metadata. These packets,
        // for all logical streams, should be grouped together after the identification packets.
        // Reading pages consumes these headers and returns any relevant data as side data. Read
        // pages until all headers are consumed and the first bitstream packets are buffered.
        while (true)
        {
            var page = _pages.Page();
            if (streams.TryGetValue(page.Header.Serial, out var stream))
            {
                var sideData = stream.ReadPage(page);

                //Consume each piece of side data
                foreach (var data in sideData)
                {
                    switch (data)
                    {
                        case ISideData.MetadataSideData metadata:
                            _metadata.Revisions.Enqueue(metadata.Metadata);
                            break;
                    }
                }

                if (stream.HasPackets)
                    break;
            }


            // The current page has been consumed and we're committed to reading a new one. Record
            // the end of the current page.
            byteRangeStart = _reader.Pos();

            _pages.TryNextPage(_reader);
        }

        // Probe the logical streams for their start and end pages.
        Physical.PhysicalStream.ProbeStreamStart(_reader, _pages, streams);

        ulong? byteRangeEnd = null;

        // If the media source stream is seekable, then try to determine the duration of each
        if (_reader.CanSeek())
        {
            var totallen = _reader.ByteLen();
            if (totallen.HasValue)
            {
                byteRangeEnd = Physical.PhysicalStream.ProbeStreamEnd(
                    _reader,
                    _pages,
                    streams,
                    byteRangeStart,
                    (ulong)totallen.Value
                );
            }
        }

        // At this point it can safely be assumed that a new physical stream is starting.
        _tracks.Clear();

        // Second, add a track for all streams.
        foreach (var stream in streams)
        {
            // Warn if the track is not ready. This should not happen if the physical stream was
            // muxed properly.
            if (!stream.Value.IsReady)
            {
                Debug.WriteLine($"Stream with serial={stream.Key:x8} is not ready");
            }

            _tracks.Add(new Track(stream.Key, stream.Value.CodecParams, null));
        }

        _streams = streams;

        // Last, store the lower and upper byte boundaries of the physical stream for seeking.
        _physByteRangeStart = byteRangeStart;
        _physByteRangeEnd = byteRangeEnd;
    }

    public Packet NextPacket() => NextLogicalPacket();

    private Packet NextLogicalPacket()
    {
        while (true)
        {
            var page = _pages.Page();

            // Read the next packet. Packets are only ever buffered in the logical stream of the
            // current page.
            if (_streams.TryGetValue(page.Header.Serial, out var stream))
            {
                if (stream.TryNextPacket(out var pkt))
                {
                    return pkt;
                }
            }

            ReadPage();
        }
    }

    private bool ReadPage()
    {
        // Try reading pages until a page is successfully read, or an IO error.
        while (true)
        {
            try
            {
                _pages.TryNextPage(_reader);
                break;
            }
            catch (IOException)
            {
                throw;
            }
            catch (System.Exception x)
            {
                Debug.WriteLine($"Error reading page: {x.Message}");
            }
        }

        var page = _pages.Page();

        // If the page is marked as a first page, then try to start a new physical stream.
        if (page.Header.IsFirstPage)
        {
            StartNewPhysicalStream();
            return true;
        }

        if (_streams.TryGetValue(page.Header.Serial, out var stream))
        {
            // TODO: Process side data.
            var sideData = stream.ReadPage(page);
        }
        else
        {
            // If there is no associated logical stream with this page, then this is a
            // completely random page within the physical stream. Discard it.
        }

        return false;
    }

    public void Dispose()
    {
        _reader.Dispose();
    }
}