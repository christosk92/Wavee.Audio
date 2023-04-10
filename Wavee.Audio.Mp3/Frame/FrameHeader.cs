using Wavee.Audio.Codecs;
using Wavee.Audio.Mp3.Channel;
using Wavee.Audio.Mp3.Header;

namespace Wavee.Audio.Mp3.Frame;

internal record FrameHeader(
    MpegVersion Version,
    MpegLayer Layer,
    uint Bitrate,
    uint SampleRate,
    int SampleRateIdx,
    IChannelMode ChannelMode,
    Emphasis Emphasis,
    bool IsCopyrighted,
    bool IsOriginal,
    bool HasPadding,
    bool HasCrc,
    int FrameSize
)
{
    public int NumberOfChannels()
    {
        return ChannelMode switch
        {
            MonoChannelMode => 1,
            _ => 2
        };
    }

    public CodecType Codec() => Layer switch
    {
        MpegLayer.Layer1 => CODEC_TYPE_MP1,
        MpegLayer.Layer2 => CODEC_TYPE_MP2,
        MpegLayer.Layer3 => CODEC_TYPE_MP3,
        _ => throw new ArgumentOutOfRangeException()
    };

    private static CodecType CODEC_TYPE_MP1 = new CodecType(0x1001);
    private static CodecType CODEC_TYPE_MP2 = new CodecType(0x1002);
    private static CodecType CODEC_TYPE_MP3 = new CodecType(0x1003);

    public int SideInfoLength()
    {
        return Version switch
        {
            MpegVersion.Mpeg1 when ChannelMode is MonoChannelMode => 17,
            MpegVersion.Mpeg1 => 32,
            MpegVersion.Mpeg2 or MpegVersion.Mpeg2p5 when ChannelMode is MonoChannelMode => 9,
            _ => 17
        };
    }
}