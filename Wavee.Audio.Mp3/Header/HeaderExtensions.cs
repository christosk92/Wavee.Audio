using Wavee.Audio.Audio;
using Wavee.Audio.Codecs;
using Wavee.Audio.Mp3.Channel.Joint;
using Wavee.Audio.Mp3.Frame;

namespace Wavee.Audio.Mp3.Header;

internal static class HeaderExtensions
{
    public static ulong Duration(this FrameHeader header)
    {
        return header.Layer switch
        {
            MpegLayer.Layer1 => 384,
            MpegLayer.Layer2 => 1152,
            MpegLayer.Layer3 => (ulong)(576 * header.NGranules()),
        };
    }

    public static int NGranules(this FrameHeader header)
    {
        return header.Version switch
        {
            MpegVersion.Mpeg1 => 2,
            _ => 1
        };
    }

    public static SignalSpec Spec(this FrameHeader header)
    {
        var layout = header.NumberOfChannels() switch
        {
            1 => Layout.Mono,
            2 => Layout.Stereo,
            _ => throw new NotSupportedException()
        };
        return SignalSpec.NewWithLayout(
            rate: header.SampleRate,
            layout: layout
        );
    }

    public static bool IsIntensityStereo(this FrameHeader header)
    {
        return header.ChannelMode switch
        {
            JointStereoChannelMode { JointStereoMode: IntensityJointStereoMode } => true,
            JointStereoChannelMode { JointStereoMode: Layer3JointStereoMode i } => i.Intensity,
            _ => false
        };
    }
}