namespace Wavee.Audio.Mp3.Header;

/// <summary>
/// The channel mode.
/// </summary>
public enum ChannelMode
{
    /// <summary>
    ///  Single mono audio channel.
    /// </summary>
    Mono,

    /// <summary>
    ///  Dual mono audio channels.
    /// </summary>
    DualMono,

    /// <summary>
    ///  Stereo channels.
    /// </summary>
    Stereo,
    JointStereo
}