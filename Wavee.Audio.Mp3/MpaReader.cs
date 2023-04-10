using System.Buffers.Binary;
using System.Diagnostics;
using Wavee.Audio.Codecs;
using Wavee.Audio.Formats;
using Wavee.Audio.Helpers.Extensions;
using Wavee.Audio.IO;
using Wavee.Audio.Meta.Metadata;
using Wavee.Audio.Mp3.Frame;
using Wavee.Audio.Mp3.Header;
using Wavee.Audio.Mp3.Header.InfoTags;

namespace Wavee.Audio.Mp3;

public sealed class MpaReader : IFormatReader
{
    private readonly MediaSourceStream _reader;
    private readonly List<Track> _tracks;
    private readonly List<Cue> _cues;
    private readonly MetadataLog _metadata;
    private readonly FormatOptions _options;
    private ulong _firstPacketPos;
    private ulong _nextPacketTs;

    public MpaReader(MediaSourceStream source, FormatOptions options)
    {
        // Try to read the first MPEG frame.
        var (header, packet) = ReadMpegFrameStrict(source);

        // Use the header to populate the codec parameters.
        var parameters = new CodecParameters
        {
            Codec = header.Codec(),
            SampleRate = header.SampleRate,
            TimeBase = new TimeBase(1, header.SampleRate),
            Channels = header.ChannelMode.Mode switch
            {
                ChannelMode.Mono => Channels.FRONT_LEFT,
                _ => Channels.FRONT_LEFT | Channels.FRONT_RIGHT
            }
        };

        // Check if there is a Xing/Info tag contained in the first frame.
        if (Xing.TryReadInfoTag(packet, header, out var infoTag))
        {
            // THe lame tag contains replaygain and padding information
            uint delay = 0, padding = 0;
            if (infoTag!.Lame is not null)
            {
                parameters.Delay = infoTag.Lame.EncDelay;
                parameters.Padding = infoTag.Lame.EncPadding;
                delay = infoTag.Lame.EncDelay;
                padding = infoTag.Lame.EncPadding;
            }

            // The base Xing/Info tag may contain the number of frames.
            if (infoTag!.NumFrames is not null)
            {
                Debug.WriteLine("Using xing header for duration.");
                var numFrames = infoTag.NumFrames.Value * header.Duration();

                // Adjust for gapless playback.
                if (options.EnableGapless)
                {
                    parameters.NFrames = numFrames - delay - padding;
                }
                else
                {
                    parameters.NFrames = numFrames;
                }
            }
        }
        else if (Vbri.TryReadVbriTag(packet, header, out var vbriTag))
        {
            Debug.WriteLine("Using vbri header for duration.");

            var numFrames = vbriTag!.NumMpegFrames * header.Duration();

            parameters.NFrames = numFrames;
        }
        else
        {
            // The first frame was not a Xing/Info header, rewind back to the start of the frame so
            // that it may be decoded.
            source.SeekBufferedRev(MpegHeader.MPEG_HEADER_LEN + header.FrameSize);

            // Likely not a VBR file, so estimate the duration if seekable.
            if (source.CanSeek())
            {
                Debug.WriteLine("Estimating duration.");

                if (TryEstimateNumMpegFrames(source, out var nMpegFrames))
                {
                    parameters.NFrames = nMpegFrames * header.Duration();
                }
            }
        }

        var firstPacketPos = source.Pos();

        _reader = source;
        _tracks = new List<Track>
        {
            new Track(0, parameters, null)
        };
        _cues = new List<Cue>();
        _metadata = new MetadataLog(new Queue<MetadataRevision>());
        _options = options;
        _firstPacketPos = firstPacketPos;
        _nextPacketTs = 0;
    }

    public Track DefaultTrack => _tracks[0];

    /// <summary>
    /// Reads a MPEG frame and checks if the next frame begins after the packet.
    /// </summary>
    /// <param name="reader"></param>
    /// <returns></returns>
    private MpegFrameResult
        ReadMpegFrameStrict(MediaSourceStream reader)
    {
        while (true)
        {
            // Read the next MPEG frame.
            var (header, packet) = ReadMpegFrame(reader);

            //Get the position before trying to read the next header.
            var pos = reader.Pos();

            // Read a sync word from the stream. If this read fails then the file may have ended and
            // this check cannot be performed.
            var sync = MpegHeader.ReadFrameHeaderWordNoSync(reader);

            // If the stream is not synced to the next frame's sync word, or the next frame header
            // is not parseable or similar to the current frame header, then reject the current
            // packet since the stream likely synced to random data.
            if (!MpegHeader.IsSyncWord(sync) ||
                !IsFrameHeaderSimilar(header, sync))
            {
                Debug.WriteLine("Skipping jump at {0:X}",
                    pos - (ulong)packet.Length);

                //Seek back to the second byte of the rejected packet
                //to prevent syncingto the same spot again.
                reader.SeekBufferedRev(packet.Length + MpegHeader.MPEG_HEADER_LEN - 1);
                continue;
            }

            // Jump back to the position before the next header was read.
            reader.SeekBuffered(pos);
            return new MpegFrameResult
            {
                Header = header,
                Packet = packet
            };
        }
    }

    private static MpegFrameResult
        ReadMpegFrame(MediaSourceStream reader)
    {
        MpegSyncFrameResult result = default;
        while (true)
        {
            //Sync to the next frame header.
            var sync = MpegHeader.SyncFrame(reader);

            //parse the frame header fully.
            try
            {
                var header = MpegHeader.ParseFrameHeader(sync);
                result = new MpegSyncFrameResult
                {
                    Header = header,
                    HeaderWord = sync
                };
                break;
            }
            catch (Exception x)
            {
                //If the frame header is not parseable, then the stream is not MPEG.
                //throw new FormatException("Invalid MPEG frame header");
                Debug.WriteLine("Invalid MPEG frame header");
            }
        }

        // Allocate frame buffer
        Span<byte> packet = new byte[result.Header.FrameSize + MpegHeader.MPEG_HEADER_LEN];
        Span<byte> headerWordBytes = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32BigEndian(headerWordBytes,
            result.HeaderWord);
        headerWordBytes.CopyTo(packet[0..MpegHeader.MPEG_HEADER_LEN]);

        //Read the frame body
        reader.ReadExact(packet[MpegHeader.MPEG_HEADER_LEN..]);

        return new MpegFrameResult
        {
            Header = result.Header,
            Packet = packet
        };
    }

    private static bool IsFrameHeaderSimilar(FrameHeader header, uint sync)
    {
        try
        {
            var candidate = MpegHeader.ParseFrameHeader(sync);
            return header.Version == candidate.Version
                   && header.Layer == candidate.Layer
                   && header.SampleRate == candidate.SampleRate
                   && header.NumberOfChannels() == candidate.NumberOfChannels();
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool TryEstimateNumMpegFrames(MediaSourceStream reader, out ulong? output)
    {
        const uint MAX_FRAMES = 16;
        const int MAX_LEN = 16 * 1024;

        var startPos = reader.Pos();
        int totalFrameLen = 0;
        int totalFrames = 0;

        ulong totalLen = 0;
        if (reader.ByteLen() is not { } l)
        {
            output = new ulong();
            return false;
        }
        else
        {
            totalLen = (ulong)(l - (long)startPos);
        }

        ulong? numMpegFrames = null;

        while (true)
        {
            // Read the frame header.
            try
            {
                var headerVal = reader.ReadUIntBE();

                // Parse the frame header.
                var header = MpegHeader.ParseFrameHeader(headerVal);

                // Tabulate the size.
                totalFrameLen += MpegHeader.MPEG_HEADER_LEN + header.FrameSize;
                totalFrames++;

                reader.IgnoreBytes((ulong)header.FrameSize);


                // Read up-to 16 frames, or 16kB, then calculate the average MPEG frame length, and from
                // that, the total number of MPEG frames.
                if (totalFrames >= MAX_FRAMES || totalFrameLen > MAX_LEN)
                {
                    var avgFrameLen = (double)totalFrameLen / totalFrames;
                    numMpegFrames = (ulong?)((double)totalLen / (ulong)avgFrameLen);
                    break;
                }
            }
            catch (Exception)
            {
                break;
            }
        }

        // Rewind back to the first frame seen upon entering this function.
        reader.SeekBufferedRev((int)(reader.Pos() - startPos));

        output = numMpegFrames;
        return numMpegFrames is not null;
    }

    public Packet NextPacket()
    {
        FrameHeader headerRes;
        ReadOnlySpan<byte> packetRes;
        while (true)
        {
            var mpegFrameResult = ReadMpegFrame(_reader);

            // Check if the packet contains a Xing, Info, or VBRI tag.
            if (Xing.TryReadInfoTag(mpegFrameResult.Packet, mpegFrameResult.Header, out var xingInfoTag))
            {
                // Discard the packet and tag since it was not at the start of the stream.
                Debug.WriteLine("Discarding Xing/Info/VBRI tag at {0:X}", _reader.Pos());
                continue;
            }
            else if (Vbri.TryReadVbriTag(mpegFrameResult.Packet, mpegFrameResult.Header, out var vbriTag))
            {
                // Discard the packet and tag since it was not at the start of the stream.
                Debug.WriteLine("Discarding Xing/Info/VBRI tag at {0:X}", _reader.Pos());
                continue;
            }

            headerRes = mpegFrameResult.Header;
            packetRes = mpegFrameResult.Packet;
            break;
        }

        // Each frame contains 1 or 2 granules with each granule being exactly 576 samples long.
        var ts = _nextPacketTs;
        var duration = headerRes.Duration();

        _nextPacketTs += duration;

        var packet = new Packet(
            trackId: 0,
            ts: ts,
            dur: duration,
            data: packetRes
        );
        if (_options.EnableGapless)
        {
            Utils.TrimPacket(packet,
                _tracks[0].CodecParameters.Delay ?? 0,
                _tracks[0].CodecParameters.NFrames
            );
        }

        return packet;
    }
}