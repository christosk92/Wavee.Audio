using Wavee.Audio.IO;

namespace Wavee.Audio.Vorbis.Decoding.Floors;

internal sealed class Floor0 : IFloor
{
    private Floor0Setup _setup;
    private bool _isUnused;
    private ulong _amplitutde;
    private float[] _coeffs;

    private Floor0(Floor0Setup setup, bool isUnused, ulong amplitutde, float[] coeffs)
    {
        _setup = setup;
        _isUnused = isUnused;
        _coeffs = coeffs;
        _amplitutde = amplitutde;
        //fill coeffs with 0.0
        for (int i = 0; i < coeffs.Length; ++i)
            coeffs[i] = 0.0f;
    }

    public static Floor0 Read(BitReaderRtl bs, byte identBs0Exp, byte identBs1Exp, byte maxCodebook)
    {
        var order = (byte)(bs.ReadBitsLeq32(8) + 1);
        var rate = (ushort)(bs.ReadBitsLeq32(16) + 1);
        var barkMapSize = (ushort)(bs.ReadBitsLeq32(16) + 1);
        var ampBits = (byte)bs.ReadBitsLeq32(6);
        var ampOffset = (byte)bs.ReadBitsLeq32(8);
        var numBooks = (byte)(bs.ReadBitsLeq32(4) + 1);
        var books = new byte[numBooks];

        for (int i = 0; i < numBooks; ++i)
        {
            books[i] = (byte)bs.ReadBitsLeq32(8);
            if (books[i] >= maxCodebook)
                throw new NotSupportedException("Vorbis floor 0 codebook index is invalid.");
        }

        var mapShort = Bark.Map(1 << (identBs0Exp - 1), rate, barkMapSize);
        var mapLong = Bark.Map(1 << (identBs1Exp - 1), rate, barkMapSize);

        var setup = new Floor0Setup
        {
            Order = order,
            Rate = rate,
            BarkMapSize = barkMapSize,
            AmpBits = ampBits,
            AmpOffset = ampOffset,
            Books = books,
            MapShort = mapShort,
            MapLong = mapLong
        };

        return new Floor0(setup, false, 0, new float[256]);
    }

    public void ReadChannel(BitReaderRtl bs, VorbisCodebook[] ch)
    {
        throw new NotImplementedException();
    }

    public bool IsUnused => _isUnused;
    public void Synthesis(byte bsExp, float[] floor)
    {
        throw new NotImplementedException();
    }
}

internal class Floor0Setup
{
    public byte Order { get; set; }
    public ushort Rate { get; set; }
    public ushort BarkMapSize { get; set; }
    public byte AmpBits { get; set; }
    public byte AmpOffset { get; set; }
    public int[] MapLong { get; set; }
    public int[] MapShort { get; set; }
    public byte[] Books { get; set; }
}