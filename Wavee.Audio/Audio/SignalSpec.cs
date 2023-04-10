using Wavee.Audio.Codecs;

namespace Wavee.Audio.Audio;

public sealed class SignalSpec
{
    public SignalSpec(int rate, Channels channels)
    {
        Rate = (uint)rate;
        Channels = channels;
    }

    public static SignalSpec NewWithLayout(uint rate, Layout layout)
    {
        return new SignalSpec((int)rate, layout.IntoChannels());
    }

    public uint Rate { get; }
    public Channels Channels { get; }
}

internal static class ChannelsExtensions
{
    public static Channels IntoChannels(this Layout layout)
    {
        return layout switch
        {
            Layout.Mono => Channels.FRONT_LEFT,
            Layout.Stereo => Channels.FRONT_LEFT | Channels.FRONT_RIGHT,
            Layout.TwoPointOne => Channels.FRONT_LEFT | Channels.FRONT_RIGHT | Channels.LFE1,
            Layout.FivePointOne => Channels.FRONT_LEFT | Channels.FRONT_RIGHT | Channels.FRONT_CENTER | Channels.LFE1 |
                                   Channels.REAR_LEFT | Channels.REAR_RIGHT,
        };
    }
}