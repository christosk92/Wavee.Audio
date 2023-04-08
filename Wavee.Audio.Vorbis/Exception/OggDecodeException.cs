namespace Wavee.Audio.Vorbis.Exception;

internal class OggDecodeException : System.Exception
{
    public OggDecodeException(DecodeErrorType error)
        : base(error switch
        {
            DecodeErrorType.MissingOggPageMarker => "Missing OGG page marker.",
        })
    {
        Error = error;
    }

    public OggDecodeException(string message)
        : base(message)
    {
        Error = DecodeErrorType.Unknown;
    }

    public DecodeErrorType Error { get; }
}