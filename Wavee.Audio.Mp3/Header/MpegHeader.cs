namespace Wavee.Audio.Mp3.Header;

public class MpegHeader
{
    public const int MPEG_HEADER_LEN = 4;

    /// <summary>
    /// Basically the point of this code is to parse the mpeg frame header
    /// based on a sync word.
    /// </summary>
    /// <param name="sync"></param>
    /// <returns></returns>
    /// <exception cref="NotImplementedException"></exception>
    internal static FrameHeader ParseFrameHeader(uint header)
    {
        // The MPEG audio header is structured as follows:
        //
        // 0b1111_1111 0b111v_vlly 0brrrr_hhpx 0bmmmm_coee
        // where:
        //     vv   = version, ll = layer      , y = crc
        //     rrrr = bitrate, hh = sample rate, p = padding , x  = private bit
        //     mmmm = mode   , c  = copyright  , o = original, ee = emphasis
        
        var version = (header )
    }
}