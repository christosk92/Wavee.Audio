namespace Wavee.Audio.Formats;

/// <summary>
/// A `FormatReader` is a container demuxer. It provides methods to probe a media container for
/// information and access the tracks encapsulated in the container.
///
/// Most, if not all, media containers contain metadata, then a number of packetized, and
/// interleaved codec bitstreams. These bitstreams are usually referred to as tracks. Generally,
/// the encapsulated bitstreams are independently encoded using some codec. The allowed codecs for a
/// container are defined in the specification of the container format.
///
/// While demuxing, packets are read one-by-one and may be discarded or decoded at the choice of
/// the caller. The contents of a packet is undefined: it may be a frame of video, a millisecond
/// of audio, or a subtitle, but a packet will never contain data from two different bitstreams.
/// Therefore the caller can be selective in what tracks(s) should be decoded and consumed.
///
/// `FormatReader` provides an Iterator-like interface over packets for easy consumption and
/// filtering. Seeking will invalidate the state of any `Decoder` processing packets from the
/// `FormatReader` and should be reset after a successful seek operation.
/// </summary>
public interface IFormatReader
{
    
}