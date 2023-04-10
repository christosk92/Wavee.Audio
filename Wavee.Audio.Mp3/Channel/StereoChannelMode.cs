using Wavee.Audio.Mp3.Header;

namespace Wavee.Audio.Mp3.Channel;

public record StereoChannelMode(ChannelMode Mode = ChannelMode.Stereo) : IChannelMode;