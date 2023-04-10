using System.Collections;
using Wavee.Audio.Mp3.Header;

namespace Wavee.Audio.Mp3.Layers.Layer3;

/// <summary>
/// Contains the side_info and main_data portions of a MPEG audio frame.
/// </summary>
internal class FrameData
{
    public FrameData()
    {
        MainDataBegin = 0;
        Scfsi = new bool[2][];
        for (int i = 0; i < 2; i++)
        {
            Scfsi[i] = new bool[4];
        }

        Granules = new Granule[2];
        for (int i = 0; i < 2; i++)
        {
            Granules[i] = new Granule();
        }
    }

    /// <summary>
    /// The byte offset into the bit resevoir indicating the location of the first bit of main_data.
    /// If 0, main_data begins after the side_info of this frame.
    /// </summary>
    public ushort MainDataBegin { get; set; }

    /// <summary>
    /// Scale factor selector information, per channel. Each channel has 4 groups of bands that may
    /// be scaled in each granule. Scale factors may optionally be used by both granules to save
    /// bits. Bands that share scale factors for both granules are indicated by a true. Otherwise,
    /// each granule must store its own set of scale factors.
    ///
    /// Mapping of array indicies to bands [0..6, 6..11, 11..16, 16..21].
    /// </summary>
    public bool[][] Scfsi { get; set; }

    /// <summary>
    /// The granules.
    /// </summary>
    public Granule[] Granules { get; set; }

    public Span<bool[]> ScfsiMut(int channels)
    {
        return Scfsi
            .AsSpan()
            [..channels];
    }

    public Span<Granule> GranulesMut(MpegVersion version)
    {
        return version is MpegVersion.Mpeg1
            ? Granules.AsSpan(0, 2)
            : Granules.AsSpan(0, 1);
    }
    
}