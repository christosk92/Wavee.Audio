namespace Wavee.Audio.Codecs.Verification;

public readonly record struct Md5(byte[] Number) : IVerificationCheck;