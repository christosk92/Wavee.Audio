using  System.Runtime.InteropServices;
using Wavee.Audio.Codecs;
using Wavee.Audio.Formats;
using Wavee.Audio.IO;

namespace Wavee.Audio.Vorbis.Convenience;

public sealed class VorbisReader : IDisposable
{
    private readonly bool _disposeOnClose;
    private readonly Stream _reader;
    private readonly MediaSourceStream _mediaSourceStream;

    private readonly OggReader _oggReader;
    private readonly VorbisDecoder _decoder;

    private SampleBuffer<float>? _sampleBuffer;

    public VorbisReader(Stream reader,
        bool gapless,
        bool disposeOnClose)
    {
        _reader = reader;
        _disposeOnClose = disposeOnClose;
        _mediaSourceStream = new MediaSourceStream(reader,
            new MediaSourceStreamOptions(64 * 1024));
        _oggReader = new OggReader(_mediaSourceStream, new FormatOptions(
            EnableGapless: gapless
        ));
        _decoder = new VorbisDecoder(_oggReader.DefaultTrack!.CodecParameters,
            new DecoderOptions(false));
    }

    public int SampleRate => (int)_oggReader.DefaultTrack!.CodecParameters.SampleRate!.Value;
    public int Channels => (int)_oggReader.DefaultTrack!.CodecParameters.Channels!.Value.Count();

    public ReadOnlySpan<byte> ReadSamples()
    {
        try
        {
            var packet = _oggReader.NextPacket();
            var decoded = _decoder.Decode(packet);
            if (_sampleBuffer is null)
            {
                _sampleBuffer = new SampleBuffer<float>(
                    (ulong)decoded.Capacity(),
                    decoded.Spec());
            }

            _sampleBuffer.CopyInterleavedRef(decoded);
            var samples = _sampleBuffer.Samples();
            var bytes = MemoryMarshal.Cast<float, byte>(samples);
            return bytes;
        }
        catch (EndOfStreamException)
        {
            return ReadOnlySpan<byte>.Empty;
        }
    }

    public void Seek(TimeSpan to)
    {
        _oggReader.Seek(new SeekToTime(SeekMode.Accurate, to, null));
    }

    public void Dispose()
    {
        if (_disposeOnClose)
        {
            _reader.Dispose();
        }
    }
}