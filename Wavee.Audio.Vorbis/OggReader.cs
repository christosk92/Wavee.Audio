using System.Diagnostics;
using Wavee.Audio.Codecs;
using Wavee.Audio.Formats;
using Wavee.Audio.IO;
using Wavee.Audio.Meta.Metadata;
using Wavee.Audio.Vorbis.Exception;
using Wavee.Audio.Vorbis.Logical;
using Wavee.Audio.Vorbis.Mapping;
using Wavee.Audio.Vorbis.Pages;

namespace Wavee.Audio.Vorbis;

/// <summary>
/// OGG demultiplexer.
///
/// <see cref="OggReader"/> implements a demuxer for Xiph's OGG container format.
/// </summary>
public sealed class OggReader : IFormatReader, IDisposable
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

    public SeekedTo Seek(SeekToTime time)
    {
        // Get the timestamp of the desired audio frame.
        uint? serial = default;
        if (time.TrackId is not null)
            serial = time.TrackId.Value;
        else if (DefaultTrack is not null)
            serial = DefaultTrack.Id;
        else throw new ArgumentOutOfRangeException(nameof(time.TrackId));

        // Convert the time to a timestamp.
        if (_streams.TryGetValue(serial.Value, out var stream))
        {
            var parameters = stream.CodecParams;

            if (parameters.SampleRate is null)
                throw new InvalidOperationException("Stream is not  seekable because no sample rate is defined.");

            var ts = new TimeBase(1, parameters.SampleRate.Value)
                .CalcTimestamp(time.Time);

            if (ts < parameters.StartTs)
                throw new ArgumentOutOfRangeException(nameof(time.Time));

            if (parameters.NFrames is ulong nFrames)
            {
                if (ts > (nFrames + parameters.StartTs))
                    throw new ArgumentOutOfRangeException(nameof(time.Time));
            }

            return InternalSeek(ts, serial.Value);
        }

        throw new ArgumentOutOfRangeException(nameof(time.TrackId));
    }

    public SeekedTo Seek(SeekToTimestamp ts)
    {
        // Frame timestamp given.
        if (_streams.TryGetValue(ts.TrackId, out var stream))
        {
            var parameters = stream.CodecParams;

            // Timestamp lower-bound out-of-range.
            if (ts.Timestamp < parameters.StartTs)
            {
                throw new ArgumentOutOfRangeException(nameof(ts.Timestamp));
            }

            // Timestamp
            // upper-bound out-of-range.
            if (parameters.NFrames is ulong nFrames)
            {
                if (ts.Timestamp > (nFrames + parameters.StartTs))
                {
                    throw new ArgumentOutOfRangeException(nameof(ts.Timestamp));
                }
            }

            return InternalSeek(ts.Timestamp, ts.TrackId);
        }

        throw new ArgumentOutOfRangeException(nameof(ts.TrackId));
    }

    private SeekedTo InternalSeek(ulong ts, uint tarckId)
    {
        Debug.WriteLine($"Seeking to timestamp {ts} on track {tarckId}");

        // If the reader is seekable, then use the bisection method to coarsely seek to the nearest
        // page that ends before the required timestamp.
        if (_reader.CanSeek())
        {
            var stream = _streams[tarckId];

            // The end of the physical stream.
            var physicalEnd = _physByteRangeEnd.Value;

            var startBytePos = _physByteRangeStart;
            var endBytePos = physicalEnd;

            // Bisection
            while (true)
            {
                // Find the middle of the upper and lower byte search range.
                var midBytePos = (startBytePos + endBytePos) / 2;

                // Seek to the middle of the byte range.
                _reader.Seek(SeekOrigin.Begin, midBytePos);

                // Read the next page.
                _pages.NextPageForSerial(_reader, tarckId);

                // Probe the page to get the start and end timestamp.
                var (startTs, endTs)
                    = stream.InspectPage(_pages.Page());

                /*  debug!(
                    "seek: bisect step: page={{ start={}, end={} }} byte_range=[{}..{}], mid={}",
                    start_ts, end_ts, start_byte_pos, end_byte_pos, mid_byte_pos,
                );
                */
                Debug.WriteLine(
                    $"seek: bisect step: page={{ start={startTs}, end={endTs} }} byte_range=[{startBytePos}..{endBytePos}], mid={midBytePos}");

                if (ts < startTs)
                {
                    // The timestamp is before the start of the page. Move the upper byte range
                    // to the middle of the current byte range.
                    endBytePos = midBytePos;
                }
                else if (ts > endTs)
                {
                    // The timestamp is after the end of the page. Move the lower byte range
                    // to the middle of the current byte range.
                    startBytePos = midBytePos;
                }
                else
                {
                    // The sample with the required timestamp is contained in page1. Return the
                    // byte position for page0, and the timestamp of the first sample in page1, so
                    // that when packets from page1 are read, those packets will have a non-zero
                    // base timestamp.
                    break;
                }

                // Prevent infinite iteration and too many seeks when the search range is less
                // than 2x the maximum page size.
                if (endBytePos - startBytePos < 2 * OggPageHeader.OGG_PAGE_MAX_SIZE)
                {
                    // Seek to the start of the byte range.
                    _reader.Seek(SeekOrigin.Begin, startBytePos);

                    // Read the next page.
                    _pages.NextPageForSerial(_reader, tarckId);
                    break;
                }
            }

            // Reset all logical bitstreams since the physical stream will be reading from a new
            // location now.
            foreach (var kvp in _streams)
            {
                var s = kvp.Key;
                kvp.Value.Reset();

                // Read in the current page since it contains our timestamp.
                if (s == tarckId)
                {
                    kvp.Value.ReadPage(_pages.Page());
                }
            }
        }

        // Consume packets until reaching the desired timestamp.
        ulong actualTs;
        while (true)
        {
            var packet = PeekLogicalPacket();
            if (packet is not null)
            {
                if (packet.TrackId == tarckId && packet.Ts + packet.Dur > ts)
                {
                    actualTs = packet.Ts;
                    break;
                }

                DiscardLogicalPacket();
            }
            else
            {
                ReadPage();
            }
        }

        /*
        debug!(
            "seeked track={:#x} to packet_ts={} (delta={})",
            serial,
            actual_ts,
            actual_ts as i64 - required_ts as i64
        );*/
        Debug.WriteLine(
            $"seeked track={tarckId:X} to packet_ts={actualTs} (delta={actualTs - ts})");

        return new SeekedTo(
            TrackId: tarckId,
            RequiredTs: ts,
            ActualTs: actualTs
        );
    }

    private void DiscardLogicalPacket()
    {
        var page = _pages.Page();
        if (_streams.TryGetValue(page.Header.Serial, out var stream))
        {
            stream.ConsumePacket();
        }
    }

    private Packet? PeekLogicalPacket()
    {
        var page = _pages.Page();

        if (_streams.TryGetValue(page.Header.Serial, out var stream))
        {
            return stream.PeekPacket();
        }

        return null;
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

public enum SeekMode
{
    Coarse,
    Accurate
}

public record struct SeekToTime(SeekMode Mode, TimeSpan Time, uint? TrackId);

public record struct SeekToTimestamp(SeekMode Mode, ulong Timestamp, uint TrackId);

public record SeekedTo(uint TrackId, ulong RequiredTs, ulong ActualTs);