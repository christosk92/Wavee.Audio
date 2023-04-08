namespace Wavee.Audio.Meta.TagVal;

public readonly record struct UnsignedIntegerTagValue(ulong Value) : ITagValue;