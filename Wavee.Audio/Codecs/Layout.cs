namespace Wavee.Audio.Codecs;


/// <summary>
/// Describes common audio channel configurations.
/// </summary>
public enum Layout
{
    /// <summary>
    /// Single centre channel.
    /// </summary>
    Mono,
    
    /// <summary>
    /// Left and Right channels.
    /// </summary>
    Stereo,
    
    /// <summary>
    /// Left and Right channels with a single low-frequency channel.
    /// </summary>
    TwoPointOne,
    
    /// <summary>
    /// Front Left and Right, Rear Left and Right, and a single low-frequency channel.
    /// </summary>
    FivePointOne
}