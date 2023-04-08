using Wavee.Audio.Codecs.Verification;

namespace Wavee.Audio.Codecs;

/// <summary>
/// Codec parameters stored in a container format's headers and metadata may be passed to a codec
/// using the `CodecParameters` structure.
/// </summary>
public record CodecParameters
{
    /// <summary>The codec type.</summary>
    public CodecType Codec { get; init; }

    /// <summary>The sample rate of the audio in Hz.</summary>
    public uint? SampleRate { get; init; }

    /// <summary>
    /// The timebase of the stream.
    /// The timebase is the length of time in seconds of a single tick of a timestamp or duration.
    /// It can be used to convert any timestamp or duration related to the stream into seconds.
    /// </summary>
    public TimeBase? TimeBase { get; init; }

    /// <summary>
    /// The length of the stream in number of frames.
    /// If a timebase is available, this field can be used to calculate the total duration of the
    /// stream in seconds by using [`TimeBase::calc_time`] and passing the number of frames as the
    /// timestamp.
    /// </summary>
    public ulong? NFrames { get; set; }

    /// <summary>The timestamp of the first frame.</summary>
    public ulong StartTs { get; set; }

    /// <summary>The sample format of an audio sample.</summary>
    public SampleFormat? SampleFormat { get; init; }

    /// <summary>The number of bits per one decoded audio sample.</summary>
    public uint? BitsPerSample { get; init; }

    /// <summary>The number of bits per one encoded audio sample.</summary>
    public uint? BitsPerCodedSample { get; init; }

    /// <summary>A bitmask of all channels in the stream.</summary>
    public Channels? Channels { get; init; }

    /// <summary>The channel layout.</summary>
    public Layout? ChannelLayout { get; init; }

    /// <summary>The number of leading frames inserted by the encoder that should be skipped during playback.</summary>
    public uint? Delay { get; set; }

    /// <summary>
    /// The number of trailing frames inserted by the encoder for padding that should be skipped
    /// during playback.
    /// </summary>
    public uint? Padding { get; set; }

    /// <summary>The maximum number of frames a packet will contain.</summary>
    public ulong? MaxFramesPerPacket { get; init; }

    /// <summary>The demuxer guarantees packet data integrity.</summary>
    public bool PacketDataIntegrity { get; init; }

    /// <summary>A method and expected value that may be used to perform verification on the decoded audio.</summary>
    public IVerificationCheck? VerificationCheck { get; init; }

    /// <summary>The number of frames per block, in case packets are seperated in multiple blocks.</summary>
    public ulong FramesPerBlock { get; init; }

    /// <summary>Extra data (defined by the codec).</summary>
    public Memory<byte>? ExtraData { get; set; }
    
}