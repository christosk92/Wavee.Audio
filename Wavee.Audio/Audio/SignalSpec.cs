using Wavee.Audio.Codecs;

namespace Wavee.Audio.Audio;

public sealed class SignalSpec
{
    public SignalSpec(int rate, Channels channels)
    {
        Rate = (uint)rate;
        Channels = channels;
    }

    public uint Rate { get; }
    public Channels Channels { get; }
}