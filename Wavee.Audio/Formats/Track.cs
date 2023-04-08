using Wavee.Audio.Codecs;

namespace Wavee.Audio;

public record Track(uint Id,
    CodecParameters CodecParameters,
    string? Language);