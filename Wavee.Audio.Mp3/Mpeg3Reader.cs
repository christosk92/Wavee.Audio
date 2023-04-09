using System.Buffers.Binary;
using System.Diagnostics;
using Wavee.Audio.Formats;
using Wavee.Audio.IO;
using Wavee.Audio.Meta.Metadata;
using Wavee.Audio.Mp3.Header;

namespace Wavee.Audio.Mp3;

public class Mpeg3Reader : IFormatReader
{
    private readonly MediaSourceStream _reader;
    private readonly List<Track> _tracks;
    private readonly List<Cue> _cues;
    private readonly MetadataLog _metadata;
    private readonly FormatOptions _options;
    private ulong _firstPacketPos;
    private ulong _nextPacketTs;

    public Mpeg3Reader(MediaSourceStream source, FormatOptions options)
    {
        // Try to read the first MPEG frame.
        var (header, packet) = ReadMpegFrameStrict(source);
    }


    /// <summary>
    /// Reads a MPEG frame and checks if the next frame begins after the packet.
    /// </summary>
    /// <param name="source"></param>
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
            if (reader.TryReadFrameHeaderWordNoSync(out var sync))
            {
                // If the stream is not synced to the next frame's sync word, or the next frame header
                // is not parseable or similar to the current frame header, then reject the current
                // packet since the stream likely synced to random data.
                if (!MpegHeaderUtils.IsSyncWord(sync) ||
                    !IsFrameHeaderSimilar(header, sync))
                {
                    Debug.WriteLine("Skipping jump at {0:X}",
                        pos - (ulong)packet.Length);

                    //Seek back to the second byte of the rejected packet
                    //to prevent syncingto the same spot again.
                    reader.SeekBufferedRev(packet.Length + MpegHeader.MPEG_HEADER_LEN - 1);
                    continue;
                }
            }
        }
    }

    private MpegFrameResult
        ReadMpegFrame(MediaSourceStream reader)
    {
        MpegSyncFrameResult result = default;
        while (true)
        {
            //Sync to the next frame header.
            var sync = MpegHeaderUtils.SyncFrame(reader);

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
            catch (Exception)
            {
                //If the frame header is not parseable, then the stream is not MPEG.
                throw new FormatException("Invalid MPEG frame header");
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

    private bool IsFrameHeaderSimilar(FrameHeader header, uint sync)
    {
    }
}

internal class FrameHeader
{
}