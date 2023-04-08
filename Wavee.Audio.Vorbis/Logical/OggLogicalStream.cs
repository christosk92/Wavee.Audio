using System.Buffers;
using System.Collections;
using System.Diagnostics;
using Wavee.Audio.Codecs;
using Wavee.Audio.Formats;
using Wavee.Audio.Vorbis.Mapping;
using Wavee.Audio.Vorbis.Mapping.Mappers;
using Wavee.Audio.Vorbis.Pages;
using Wavee.Audio.Vorbis.Physical;

namespace Wavee.Audio.Vorbis.Logical;

internal class OggLogicalStream
{
    private readonly IMapper? _mapper;
    private Queue<Packet>? _packets;
    private byte[] _partBuf;
    private int _partLen;
    private PageInfo? _prevPageInfo;
    private Bound? _startBound;
    private Bound? _endBound;
    private bool _gapless;

    public OggLogicalStream(IMapper mapper, bool gapless)
    {
        _packets = new Queue<Packet>();
        _mapper = mapper;
        _partBuf = Array.Empty<byte>();
        _partLen = 0;
        _prevPageInfo = null;
        _startBound = null;
        _endBound = null;
        _gapless = gapless;
    }

    public bool HasPackets => _packets?.Count > 0;
    public bool IsReady => _mapper.IsReady;
    public CodecParameters CodecParams => _mapper.CodecParams();

    /// <summary>
    /// Reads a page of data from the logical stream.
    /// </summary>
    /// <param name="page"></param>
    /// <returns></returns>
    /// <exception cref="NotImplementedException"></exception>
    public IEnumerable<ISideData> ReadPage(OggPage page)
    {
        // Side data vector. This will not allocate unless data is pushed to it (normal case).
        var sideData = new List<ISideData>();

        // If the last sequence number is available, detect non-monotonicity and discontinuities
        // in the stream. In these cases, clear any partial packet data.
        if (_prevPageInfo.HasValue)
        {
            var lastTs = _prevPageInfo.Value;
            if (page.Header.Sequence < lastTs.Seq)
            {
                Debug.WriteLine("detected stream page non-monotonicity");
                _partLen = 0;
            }
            else if (page.Header.Sequence - lastTs.Seq > 1)
            {
                Debug.WriteLine($"detected stream discontinuity of {page.Header.Sequence - lastTs.Seq} page(s)");
                _partLen = 0;
            }
        }


        _prevPageInfo = new PageInfo { Seq = page.Header.Sequence, AbsGp = page.Header.AbsGp };

        using var iter = page.Packets();

        // If there is partial packet data buffered, a continuation page is expected.
        if (!page.Header.IsContinuation && _partLen > 0)
        {
            Console.WriteLine("expected a continuation page");

            // Clear partial packet data.
            _partLen = 0;
        }

        // If there is no partial packet data buffered, a continuation page is not expected.
        if (page.Header.IsContinuation && _partLen == 0)
        {
            // If the continuation page contains packets, drop the first packet since it would
            // require partial packet data to be complete. Otherwise, ignore this page entirely.
            if (page.NumPacket > 0)
            {
                Console.WriteLine("unexpected continuation page, ignoring incomplete first packet");
                iter.MoveNext();
            }
            else
            {
                Console.WriteLine("unexpected continuation page, ignoring page");
                return sideData;
            }
        }

        int numPrevPackets = _packets.Count;

        while (iter.MoveNext())
        {
            var buf = iter.Current;

            // Get a packet with data from the partial packet buffer, the page, or both.
            var data = GetPacket(buf.Span);

            // Perform packet mapping. If the packet contains stream data, queue it onto the packet
            // queue. If it contains side data, then add it to the side data list. Ignore other
            // types of packet data.
            var result = _mapper.MapPacket(data);
            switch (result)
            {
                case IMapResult.StreamData streamData:
                    _packets.Enqueue(new Packet(page.Header.Serial, 0, streamData.Dur, data));
                    break;

                case IMapResult.SideData sideDataItem:
                    sideData.Add(sideDataItem.Data);
                    break;

                case IMapResult.ErrorData error:
                    Debug.WriteLine($"mapping packet failed ({error.Error.Data}), skipping");
                    break;
                default:
                    break;
            }
        }

        if (iter.PartialPacket is not null)
        {
            SavePartialPacket(iter.PartialPacket.Value.Span);
        }

        // The number of packets from this page that were queued.
        int numNewPackets = _packets.Count - numPrevPackets;

        if (numNewPackets > 0)
        {
            // Get the start delay.
            ulong startDelay = _startBound.HasValue ? _startBound.Value.Delay : 0;

            // Assign timestamps by first calculating the timestamp of one past the last sample in
            // in the last packet of this page, add the start delay.
            ulong pageEndTs = _mapper.AbsGpToTs(page.Header.AbsGp) + startDelay;
            // If this is the last page, then add the end delay to the timestamp.
            if (page.Header.IsLastPage)
            {
                ulong endDelay = _endBound.HasValue ? _endBound.Value.Delay : 0;
                pageEndTs += endDelay;
            }

            // Then, iterate over the newly added packets in reverse order and subtract their
            // cumulative duration at each iteration to get the timestamp of the first sample
            // in each packet.
            ulong pageDur = 0;

            foreach (var packet in _packets.Reverse().Take(numNewPackets))
            {
                pageDur += packet.Dur;
                packet.Ts = pageEndTs - pageDur;
            }

            if (_gapless)
            {
                foreach (var packet in _packets.Reverse().Take(numNewPackets))
                {
                    Formats.Utils.TrimPacket(packet, (uint)startDelay, _endBound?.Ts);
                }
            }
        }

        return sideData;
    }

    private void SavePartialPacket(ReadOnlySpan<byte> buf)
    {
        var newPartLen = _partLen + buf.Length;

        if (newPartLen > _partBuf.Length)
        {
            // Do not exceed an a certain limit to prevent unbounded memory growth.
            if (newPartLen > MAX_PACKET_LEN)
            {
                throw new InvalidOperationException("partial packet size limit exceeded");
            }

            // New partial packet buffer size, rounded up to the nearest 8K block.
            //            let new_buf_len = (new_part_len + (8 * 1024 - 1)) & !(8 * 1024 - 1);
            var newBufLen = (newPartLen + (8 * 1024 - 1)) & ~(8 * 1024 - 1);
            Debug.WriteLine($"expanding partial packet buffer to {newBufLen} bytes");

            Array.Resize(ref _partBuf, newBufLen);
            for (int i = _partLen; i < newBufLen; i++)
            {
                _partBuf[i] = 0;
            }
        }

        buf.CopyTo(_partBuf.AsSpan()[_partLen..newPartLen]);
        _partLen = newPartLen;
    }

    private const int MAX_PACKET_LEN = 8 * 102 * 1024;

    private ReadOnlySpan<byte> GetPacket(ReadOnlySpan<byte> packetBuf)
    {
        if (_partLen == 0)
        {
            return packetBuf;
        }
        else
        {
            Span<byte> buf = new byte[_partLen + packetBuf.Length];

            // Split the buffer into two portions: saved and new.
            var vec0 = buf[.._partLen];
            var vec1 = buf[_partLen..];

            // Copy and consume the saved partial packet.
            _partBuf.AsSpan(0, _partLen).CopyTo(vec0);
            _partLen = 0;

            // Read the remainder of the partial packet from the page.
            packetBuf.CopyTo(vec1);

            return buf;
        }
    }

    internal readonly record struct PageInfo(uint Seq, ulong AbsGp);

    public void InspectStartPage(OggPage page)
    {
        if (_startBound.HasValue)
        {
            Debug.WriteLine("start page already set");
            return;
        }

        if (!_mapper.TryMakeParser(out var parser))
        {
            Debug.WriteLine("no parser available");
            return;
        }

        // Calculate the page duration.
        ulong pageDur = 0;

        using var iter = page.Packets();
        while (iter.MoveNext())
        {
            var buf = iter.Current;
            var packetDur = parser.ParseNextPacketDur(buf.Span);
            pageDur = (uint)Math.Min((ulong)pageDur + packetDur,
                uint.MaxValue);
        }

        var pageEndTs = _mapper.AbsGpToTs(page.Header.AbsGp);

        // If the page timestamp is >= the page duration, then the stream starts at timestamp 0 or
        // a positive start time.
        Bound? bound;
        if (pageEndTs >= pageDur)
        {
            bound = new Bound(
                Seq: page.Header.Sequence,
                Ts: pageEndTs - pageDur,
                Delay: 0
            );
        }
        else
        {
            // If the page timestamp < the page duration, then the difference is the start delay.
            bound = new Bound(
                Seq: page.Header.Sequence,
                Ts: 0,
                Delay: pageDur - pageEndTs
            );
        }

        // Update codec parameters.

        var codecParams = _mapper.CodecParams();
        codecParams.StartTs = bound.Value.Ts;

        if (bound.Value.Delay > 0)
        {
            codecParams.Delay = (uint)bound.Value.Delay;
        }

        _mapper.UpdateCodecParams(codecParams);
        _startBound = bound;
    }

    /// <summary>
    /// Examines one or more of the last pages of the codec bitstream to obtain the end time and
    /// end delay parameters. To obtain the end delay, at a minimum, the last two pages are
    /// required. The state returned by each iteration of this function should be passed into the
    /// subsequent iteration.
    /// </summary>
    /// <param name="state"></param>
    /// <param name="page"></param>
    /// <returns></returns>
    /// <exception cref="NotImplementedException"></exception>
    public InspectState InspectEndPage(InspectState state, OggPage page)
    {
        if (_endBound.HasValue)
        {
            Debug.WriteLine("end page already set");
            return state;
        }

        // Get and/or create the sniffer state.
        var parser = state.Parser;
        if (parser is null)
        {
            if (!_mapper.TryMakeParser(out parser))
            {
                Debug.WriteLine("no parser available");
                return state;
            }
        }

        var startDelay = _startBound?.Delay ?? 0;

        // The actual page end timestamp is the absolute granule position + the start delay.
        var pageEndTs = (long)_mapper.AbsGpToTs(page.Header.AbsGp) + (long)(_gapless ? 0 : startDelay);
        if (pageEndTs < 0)
        {
            pageEndTs = long.MaxValue;
        }

        // Calculate the page duration. Note that even though only the last page uses this duration,
        // it is important to feed the packet parser so that the first packet of the final page
        // doesn't have a duration of 0 due to lapping on some codecs.
        ulong pageDur = 0;

        using var iter = page.Packets();
        while (iter.MoveNext())
        {
            var buf = iter.Current;
            var packetDur = parser.ParseNextPacketDur(buf.Span);
            pageDur = (uint)Math.Min((ulong)pageDur + packetDur,
                uint.MaxValue);
        }

        // The end delay can only be determined if this is the last page, and the timstamp of the
        // second last page is known.
        ulong endDelay = 0;
        if (page.Header.IsLastPage && state.Bound.HasValue)
        {
            var lastBound = state.Bound.Value;
            // The real ending timestamp of the decoded data is the timestamp of the previous
            // page plus the decoded duration of this page.
            var actualEndPageTs = Math.Min(lastBound.Ts + pageDur, long.MaxValue);

            // Any samples after the stated timestamp of this page are considered delay samples.
            if (actualEndPageTs > (ulong)pageEndTs)
            {
                endDelay = actualEndPageTs - (ulong)pageEndTs;
            }
        }

        var bound = new Bound(
            Seq: page.Header.Sequence,
            Ts: (ulong)pageEndTs,
            Delay: endDelay
        );

        // If this is the last page, update the codec parameters.
        if (page.Header.IsLastPage)
        {
            var codecParams = _mapper.CodecParams();

            // Do not report the end delay if gapless is enabled.
            var blockEndTs = bound.Ts + (_gapless ? 0 : bound.Delay);

            if (blockEndTs > codecParams.StartTs)
            {
                codecParams.NFrames = (uint)(blockEndTs - codecParams.StartTs);
            }

            if (bound.Delay > 0)
            {
                codecParams.Padding = (uint)bound.Delay;
            }

            _endBound = bound;
            _mapper.UpdateCodecParams(codecParams);
        }

        state.Bound = bound;

        return state;
    }

    public bool TryNextPacket(out Packet? o)
    {
        if (_packets.TryDequeue(out var packet))
        {
            o = packet;
            return true;
        }

        o = null;
        return false;
    }
}