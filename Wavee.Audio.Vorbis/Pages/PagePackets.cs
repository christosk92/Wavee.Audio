using System.Collections;

namespace Wavee.Audio.Vorbis.Pages;

internal sealed class PagePackets : IEnumerator<Memory<byte>>
{
    private readonly IEnumerator<ushort> _lens;

    private Memory<byte> _data;
    private Memory<byte> _current;

    public PagePackets(IEnumerable<ushort> lens, byte[] data)
    {
        _lens = lens.GetEnumerator();
        _data = data;
    }


    public bool MoveNext()
    {
        var next = _lens.MoveNext();
        if (!next) return false;

        //                let (packet, rem) = self.data.split_at(usize::from(*len));

        (int len, Memory<byte> rem) = (_lens.Current, _data[_lens.Current..]);
        var packet = _data[..len];
        _current = packet;
        _data = rem;
        return true;
    }

    public void Reset()
    {
        //do nothing
    }

    public Memory<byte> Current => _current;

    object IEnumerator.Current => Current;

    public ReadOnlyMemory<byte>? PartialPacket
    {
        /// If this page ends with an incomplete (partial) packet, get a slice to the data associated
        /// with the partial packet.
        get
        {
            int discard = 0;
            while (_lens.MoveNext())
            {
                discard += _lens.Current;
            }

            if (_data.Length > discard)
            {
                return _data.Slice(discard);
            }
            else
            {
                return null;
            }
        }
    }

    public void Dispose()
    {
        _lens.Dispose();
    }
}