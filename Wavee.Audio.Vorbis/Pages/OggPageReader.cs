using System.Buffers.Binary;
using System.Diagnostics;
using Wavee.Audio.Checksum;
using Wavee.Audio.Codecs.Verification;
using Wavee.Audio.IO;
using Wavee.Audio.Vorbis.Exception;

namespace Wavee.Audio.Vorbis.Pages;

/// <summary>
/// A reader of OGG pages.
/// </summary>
internal sealed class OggPageReader
{
    private OggPageHeader _header;
    private List<ushort> _packetLens;
    private byte[] _pageBuf;
    private int _pageBufLen;

    public OggPageReader()
    {
        _header = default;
        _packetLens = new List<ushort>();
        _pageBuf = Array.Empty<byte>();
        _pageBufLen = 0;
    }

    public OggPageHeader Header => _header;

    public OggPage Page()
    {
        if (_pageBufLen > 255 * 255)
            throw new OggDecodeException(DecodeErrorType.PageTooLarge);

        return new OggPage(_header, _packetLens, _pageBuf[.._pageBufLen]);
    }

    public static OggPageReader? TryNew<T>(T reader) where T : IReadBytes, ISeekBuffered
    {
        var pageReader = new OggPageReader();

        pageReader.TryNextPage(reader);

        return pageReader;
    }

    /// <summary>
    /// Attempts to read the next page. If the page is corrupted or invalid, returns an error.
    /// </summary>
    /// <param name="reader"></param>
    /// <returns></returns>
    /// <exception cref="NotImplementedException"></exception>
    internal void TryNextPage<T>(T reader) where T : IReadBytes, ISeekBuffered
    {
        Span<byte> headerBuf = new byte[OggPageHeader.OGG_PAGE_HEADER_SIZE];
        OggPageHeader.OGG_PAGE_MARKER
            .CopyTo(headerBuf[..4]);

        // Synchronize to an OGG page capture pattern.
        SyncPage(reader);

        // Record the position immediately after synchronization. If the page is found corrupt the
        // reader will need to seek back here to try to regain synchronization.
        var syncPos = reader.Pos();

        // Read the part of the page header after the capture pattern into a buffer.
        reader.ReadExact(headerBuf[4..]);

        // Parse the page header buffer.
        var header = OggPageHeader.ReadPageHeader(new BufReader(headerBuf));

        // The CRC of the OGG page requires the page checksum bytes to be zeroed.
        headerBuf[22..26].Clear();

        // Instantiate a Crc32, initialize it with 0, and feed it the page header buffer.
        var crc = new Crc32(0);
        crc.ProcessBufBytes(headerBuf);

        // The remainder of the page will be checksummed as it is read.
        var crcReader = new MonitorStream<T, Crc32>(
            inner: reader,
            monitor: crc
        );

        // Read segment table.
        var pageBodyLen = 0;
        var packetLen = 0;

        // TODO: Can this be transactional? A corrupt page causes the PageReader's state not
        // to change.
        _packetLens.Clear();

        for (var i = 0; i < header.NSegments; i++)
        {
            var segmentLen = crcReader.ReadByte();
            pageBodyLen += segmentLen;
            packetLen += segmentLen;


            // A segment with a length < 255 indicates that the segment is the end of a packet.
            // Push the packet length into the packet queue for the stream.
            if (segmentLen < 255)
            {
                _packetLens.Add((ushort)packetLen);
                packetLen = 0;
            }
        }

        ReadPageBody(crcReader, pageBodyLen);

        var calculatedCrc = crcReader.Monitor.Crc();

        // If the CRC for the page is incorrect, then the page is corrupt.
        if (header.Crc != calculatedCrc)
        {
            Debug.WriteLine($"OggPageReader: CRC mismatch: {header.Crc} != {calculatedCrc}");

            //Clear packet lengths.
            _packetLens.Clear();
            _pageBufLen = 0;

            //Seek back to the position after synchronization.
            crcReader.Inner.SeekBuffered(syncPos);

            throw new OggDecodeException(DecodeErrorType.CrcMismatch);
        }

        _header = header;
    }

    private void ReadPageBody<T>(T reader, int len)
        where T : IReadBytes
    {
        // This is precondition.
        if (len > 255 * 255)
            throw new ArgumentOutOfRangeException(nameof(len));

        if (len > _pageBuf.Length)
        {
            // New page buffer size, rounded up to the nearest 8K block.
            int newBufLen = (len + (8 * 1024 - 1)) & ~(8 * 1024 - 1);
            Array.Resize(ref _pageBuf, newBufLen);
        }

        _pageBufLen = len;

        reader.ReadExact(_pageBuf.AsSpan()[..len]);
    }

    private static void SyncPage<T>(T reader) where T : IReadBytes, ISeekBuffered
    {
        var marker = BinaryPrimitives.ReadUInt32BigEndian(reader.ReadQuadBytes());

        Span<byte> markerBytes = new byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32BigEndian(markerBytes, marker);

        while (!markerBytes.SequenceEqual(OggPageHeader.OGG_PAGE_MARKER))
        {
            marker <<= 8;
            marker |= (uint)reader.ReadByte();
            BinaryPrimitives.WriteUInt32BigEndian(markerBytes, marker);
        }
    }

    public bool TryGetFirstPacket(out ReadOnlySpan<byte> o)
    {
        if (_packetLens.Count == 0)
        {
            o = default;
            return false;
        }

        o = _pageBuf.AsSpan()[.._packetLens[0]];
        return true;
    }

    public void NextPage<T>(T reader) where T : IReadBytes, ISeekBuffered
    {
        while (true)
        {
            try
            {
                TryNextPage(reader);
                break;
            }
            catch (IOException io)
            {
                if (io is EndOfStreamException)
                    break;
                throw;
            }
            catch (ArgumentOutOfRangeException x)
            {
                throw;
            }
            catch (System.Exception e)
            {
                // Ignore and continue looping
            }
        }
    }

    /// <summary>
    /// Reads the next page with a specific serial.
    /// If the next page is corrupted or invalid, the
    /// page is discarded and the reader tries again
    /// until a valid page is read or end-of-stream.
    /// </summary>
    /// <param name="reader"></param>
    /// <param name="serial"></param>
    /// <typeparam name="B"></typeparam>
    public void NextPageForSerial<B>(B reader, uint serial)
    where B : IReadBytes, ISeekBuffered
    {
        while (true)
        {
            TryNextPage(reader);
            // Exit if a page with the specific serial is found.
            if (_header.Serial == serial)
                break;
        }
    }
}