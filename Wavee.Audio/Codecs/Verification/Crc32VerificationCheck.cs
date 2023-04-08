namespace Wavee.Audio.Codecs.Verification;

internal readonly record struct Crc32VerificationCheck(byte[] Number) : IVerificationCheck;