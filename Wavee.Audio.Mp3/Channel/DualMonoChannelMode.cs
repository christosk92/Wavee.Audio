using Wavee.Audio.Mp3.Header;

namespace Wavee.Audio.Mp3.Channel;

public record DualMonoChannelMode(ChannelMode Mode = ChannelMode.DualMono) : IChannelMode;