using System.Numerics;

namespace Wavee.Audio.Codecs;

[Flags]
public enum Channels : uint
{
    /// <summary>
    /// Front-left (left) or the Mono channel.
    /// </summary>
    FRONT_LEFT = 0x0000_0001,

    /// <summary>
    /// Front-right (right) channel.
    /// </summary>
    FRONT_RIGHT = 0x0000_0002,

    /// <summary>
    /// Front-centre (centre) channel.
    /// </summary>
    FRONT_CENTER = 0x0000_0004,

    /// <summary>
    /// Low frequency channel 1.
    /// </summary>
    LFE1 = 0x0000_0008,

    /// <summary>
    /// Rear-left (surround rear left) channel.
    /// </summary>
    REAR_LEFT = 0x0000_0010,

    /// <summary>
    /// /Rear-right (surround rear right) channel.
    /// </summary>
    REAR_RIGHT = 0x0000_0020,

    /// <summary>
    /// Front left-of-centre (left center) channel.
    /// </summary>
    FRONT_LEFT_CENTER = 0x0000_0040,

    /// <summary>
    /// Front right-of-centre (right center) channel.
    /// </summary>
    FRONT_RIGHT_CENTER = 0x0000_0080,

    /// <summary>
    /// Rear-centre (surround rear centre) channel.
    /// </summary>
    REAR_CENTER = 0x0000_0100,

    /// <summary>
    /// Side left (surround left) channel.
    /// </summary>
    SIDE_LEFT = 0x0000_0200,

    /// <summary>
    /// Side right (surround right) channel.
    /// </summary>
    SIDE_RIGHT = 0x0000_0400,

    /// <summary>
    /// Top centre channel.
    /// </summary>
    TOP_CENTER = 0x0000_0800,

    /// <summary>
    /// Top front-left channel.
    /// </summary>
    TOP_FRONT_LEFT = 0x0000_1000,

    /// <summary>
    /// Top centre channel.
    /// </summary>
    TOP_FRONT_CENTER = 0x0000_2000,

    /// <summary>
    /// Top front-right channel.
    /// </summary>
    TOP_FRONT_RIGHT = 0x0000_4000,

    /// <summary>
    /// Top rear-left channel.
    /// </summary>
    TOP_REAR_LEFT = 0x0000_8000,

    /// <summary>
    /// Top rear-centre channel.
    /// </summary>
    TOP_REAR_CENTER = 0x0001_0000,

    /// <summary>
    /// Top rear-right channel.
    /// </summary>
    TOP_REAR_RIGHT = 0x0002_0000,

    /// <summary>
    /// Rear left-of-centre channel.
    /// </summary>
    REAR_LEFT_CENTER = 0x0004_0000,

    /// <summary>
    /// Rear right-of-centre channel.
    /// </summary>
    REAR_RIGHT_CENTER = 0x0008_0000,

    /// <summary>
    /// Front left-wide channel.
    /// </summary>
    FONRT_LEFT_WIDE = 0x0010_0000,

    /// <summary>
    /// Front right-wide channel.
    /// </summary>
    FRONT_RIGHT_WIDE = 0x0020_0000,

    /// <summary>
    /// Front left-high channel.
    /// </summary>
    FRONT_LEFT_HIGH = 0x0040_0000,

    /// <summary>
    /// Front centre-high channel.
    /// </summary>
    FRONT_CENTER_HIGH = 0x0080_0000,

    /// <summary>
    /// Front right-high channel.
    /// </summary>
    FRONT_RIGHT_HIGH = 0x0100_0000,

    /// <summary>
    /// Low frequency channel 2.
    /// </summary>
    LFE2 = 0x0200_0000,
}

public static class ChannelsExtensions
{
    public static uint Count(this Channels channels)
    {
        return (uint)BitOperations.PopCount((uint)channels);
    }
}