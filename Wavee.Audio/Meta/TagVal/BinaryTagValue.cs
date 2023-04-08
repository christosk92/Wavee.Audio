namespace Wavee.Audio.Meta.TagVal;

public readonly record struct BinaryTagValue(byte[] Value) : ITagValue;