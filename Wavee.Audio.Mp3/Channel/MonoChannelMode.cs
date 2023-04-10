using Wavee.Audio.Mp3.Header;

namespace Wavee.Audio.Mp3.Channel;

public record MonoChannelMode(ChannelMode Mode = ChannelMode.Mono) : IChannelMode;