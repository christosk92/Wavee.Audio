namespace Wavee.Audio.Codecs.Verification;

public readonly record struct Crc8(byte Number) : IVerificationCheck;