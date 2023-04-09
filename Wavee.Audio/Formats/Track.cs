using Wavee.Audio.Codecs;

namespace Wavee.Audio.Formats;

public record Track(uint Id,
    CodecParameters CodecParameters,
    string? Language);