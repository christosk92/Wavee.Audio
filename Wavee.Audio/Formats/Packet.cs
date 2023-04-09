namespace Wavee.Audio.Formats;

/// <summary>
/// A <see cref="Packet"/> contains a discrete amount of encoded data for a single codec bitstream. The exact
/// amount of data is bounded, but not defined, and is dependant on the container and/or the
/// encapsulated codec.
/// </summary>
public record Packet
{
    /// <summary>
    /// The track id.
    /// </summary>
    public uint TrackId { get; }

    public Packet(uint trackId, ulong ts, ulong dur, ReadOnlySpan<byte> data)
    {
        TrackId = trackId;
        Ts = ts;
        Dur = dur;
        Data = data.ToArray();
        TrimEnd = 0;
        TrimStart = 0;
    }

    /// <summary>
    /// The timestamp of the packet. When gapless support is enabled, this timestamp is relative to
    /// the end of the encoder delay.
    ///
    /// This timestamp is in `TimeBase` units.
    /// </summary>
    public ulong Ts { get; set; }

    /// <summary>
    /// The duration of the packet. When gapless support is enabled, the duration does not include
    /// the encoder delay or padding.
    ///
    /// The duration is in `TimeBase` units.
    /// </summary>
    public ulong Dur { get; set; }

    /// <summary>
    /// When gapless support is enabled, this is the number of decoded frames that should be trimmed
    /// from the start of the packet to remove the encoder delay. Must be 0 in all other cases.
    /// </summary>
    public uint TrimStart { get; set; }

    /// <summary>
    /// When gapless support is enabled, this is the number of decoded frames that should be trimmed
    /// from the end of the packet to remove the encoder padding. Must be 0 in all other cases.
    /// </summary>
    public uint TrimEnd { get; set; }

    /// <summary>
    /// The packet buffer.
    /// </summary>
    public byte[] Data { get; }
}