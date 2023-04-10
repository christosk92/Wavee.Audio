using Wavee.Audio.Mp3.Header;

namespace Wavee.Audio.Mp3.Channel.Joint;

public record JointStereoChannelMode(
    IJointStereoMode JointStereoMode,
    ChannelMode Mode = ChannelMode.JointStereo) : IChannelMode;