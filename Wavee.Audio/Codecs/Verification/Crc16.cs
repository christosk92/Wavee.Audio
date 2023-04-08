namespace Wavee.Audio.Codecs.Verification;

public readonly record struct Crc16(byte[] Number) : IVerificationCheck;