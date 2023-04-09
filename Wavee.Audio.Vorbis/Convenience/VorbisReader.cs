using System.Runtime.InteropServices;
using Wavee.Audio.Codecs;
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

    public VorbisReader(Stream reader, bool disposeOnClose)
    {
        _reader = reader;
        _disposeOnClose = disposeOnClose;
        _mediaSourceStream = new MediaSourceStream(reader,
            new MediaSourceStreamOptions(64 * 1024));
        _oggReader = new OggReader(_mediaSourceStream, new FormatOptions());
        _decoder = new VorbisDecoder(_oggReader.DefaultTrack!.CodecParameters,
            new DecoderOptions(false));
    }

    public int SampleRate => (int)_oggReader.DefaultTrack!.CodecParameters.SampleRate!.Value;
    public int Channels => (int)_oggReader.DefaultTrack!.CodecParameters.Channels!.Value.Count();

    public ReadOnlySpan<byte> ReadSamples()
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


    public void Dispose()
    {
        if (_disposeOnClose)
        {
            _reader.Dispose();
        }
    }
}