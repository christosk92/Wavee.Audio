namespace Wavee.Audio.Vorbis.Exception;

internal enum DecodeErrorType
{
    Unknown,
    MissingOggPageMarker,
    InvalidOggPageVersion,
    InvalidOggPageFlags,
    CrcMismatch,
    PageNotFirst,
    PageTooLarge,
    HeaderSignatureInvalid,
    HeaderPacketTypeInvalid
}