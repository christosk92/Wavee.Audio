namespace Wavee.Audio.Codecs;

/// <summary>
/// A <see cref="TimeBase"/> is the conversion factor between time, expressed in seconds, and a `TimeStamp` or
/// `Duration`.
///
/// In other words, a `TimeBase` is the length in seconds of one tick of a `TimeStamp` or
/// `Duration`.
/// </summary>
/// <param name="Numer">The numerator.</param>
/// <param name="Denom">The denominator.</param>
public readonly record struct TimeBase(uint Numer, uint Denom);