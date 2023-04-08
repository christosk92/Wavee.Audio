namespace Wavee.Audio.Vorbis.Logical;

public readonly record struct Bound(uint Seq, ulong Ts, ulong Delay);