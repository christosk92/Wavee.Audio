using System.Diagnostics;

namespace Wavee.Audio.Mp3.Layers.Layer3;

internal class BitReservoir
{
    private Memory<byte> _buf;
    private int _len;
    private int _consumed;

    public BitReservoir()
    {
        _buf = new byte[2048];
        for (int i = 0; i < 2048; i++)
        {
            _buf.Span[i] = 0;
        }

        _len = 0;
        _consumed = 0;
    }

    public uint Fill(ReadOnlySpan<byte> pktMainData, ushort mainDataBegin)
    {
        var mainDataLen = pktMainData.Length;

        // The value `main_data_begin` indicates the number of bytes from the previous frame(s) to
        // reuse. It must always be less than or equal to maximum amount of bytes the resevoir can
        // hold taking into account the additional data being added to the resevoir.
        var mainDataEnd = mainDataBegin + mainDataLen;

        if (mainDataEnd > _buf.Length)
        {
            throw new System.Exception("main_data_begin is too large");
        }

        var unread = _len - _consumed;

        // If the offset is less-than or equal to the amount of unread data in the resevoir, shift
        // the re-used bytes to the beginning of the resevoir, then copy the main data of the
        // current packet into the resevoir.
        uint underflow = default;
        if (mainDataBegin <= unread)
        {
            // Shift all the re-used bytes as indicated by main_data_begin to the front of the
            // resevoir.
            _buf.Slice(_len - mainDataBegin, mainDataBegin).CopyTo(_buf);

            // Copy the new main data from the packet buffer after the re-used bytes.
            pktMainData.CopyTo(_buf.Span[mainDataBegin..mainDataEnd]);
            _len = mainDataEnd;
            underflow = 0;
        }
        else
        {
            // Shift all the unread bytes to the front of the resevoir. Since this is an underflow
            // condition, all unread bytes will be unconditionally reused.
            _buf.Slice(_len - unread, unread).CopyTo(_buf);

            // If the offset is greater than the amount of data in the resevoir, then the stream is
            // malformed. This can occur if the decoder is starting in the middle of a stream. This
            // is particularly common with online radio streams. In this case, copy the main data
            // of the current packet into the resevoir, then return the number of bytes that are
            // missing.
            pktMainData.CopyTo(_buf.Span[unread..(unread + mainDataLen)]);
            _len = unread + mainDataLen;

            // The number of bytes that will be missing.
            var uf = (uint)(mainDataBegin - unread);

            Debug.WriteLine($"Underflow: {uf}");
            underflow = uf;
        }

        _consumed = 0;
        return underflow;
    }

    public void Clear()
    {
        throw new NotImplementedException();
    }

    public void Consume(int len)
    {
        _consumed = Math.Min(this._len, _consumed + len);
    }

    public ReadOnlySpan<byte> BytesRef() => _buf.Span[_consumed.._len];
}