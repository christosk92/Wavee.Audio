using Wavee.Audio.Audio;
using Wavee.Audio.Codecs;
using Wavee.Audio.Formats;
using Wavee.Audio.IO;
using Wavee.Audio.Mp3.Header;
using Wavee.Audio.Mp3.Layers;
using Wavee.Audio.Mp3.Layers.Layer1;
using Wavee.Audio.Mp3.Layers.Layer2;
using Wavee.Audio.Mp3.Layers.Layer3;

namespace Wavee.Audio.Mp3;

/// <summary>
/// MPEG1 and MPEG2 audio layer 1, 2, and 3 decoder.
/// </summary>
public sealed class MpaDecoder
{
    private readonly CodecParameters _codecParameters;
    private readonly IMpaDecoderState _state;

    private AudioBuffer<float> _buf;

    public MpaDecoder(CodecParameters parameters, DecoderOptions options)
    {
        _codecParameters = parameters;
        // This decoder only supports MP1, MP2, and MP3.
        if (parameters.Codec.Number
            is not 0x1001 and not 0x1002 and not 0x1003)
            throw new ArgumentException("Invalid codec type.");

        // Create decoder state.
        _state = parameters.Codec.Number switch
        {
            0x1001 => new Layer1DecoderState(),
            0x1002 => new Layer2DecoderState(),
            0x1003 => new Layer3DecoderState(),
            _ => throw new ArgumentException("Invalid codec type.")
        };
        _buf = AudioBuffer<float>.Unused();
    }

    public AudioBuffer<float> Decode(Packet packet)
    {
        try
        {
            DecodeInner(packet);
            return _buf;
        }
        catch (System.Exception x)
        {
            // _buf.Clear();
            throw;
        }
    }

    private void DecodeInner(Packet packet)
    {
        var reader = new BufReader(packet.Data);

        var header = MpegHeader.ReadFrameHeader(reader);

        if (header.FrameSize != (int)reader.BytesAvailable())
            throw new FormatException("mpa: invalid frame size.");

        // The audio buffer can only be created after the first frame is decoded.
        if (_buf.IsUnused())
        {
            _buf = new AudioBuffer<float>(1152, header.Spec());
        }
        else
        {
            // Ensure the packet contains an audio frame with the same signal specification as the
            // buffer.
            //
            // TODO: Is it worth it to support changing signal specifications?
            if (header.Spec() != _buf.Spec())
                throw new FormatException("mpa: invalid signal specification.");
        }

        // Clear the audio buffer.
        _buf.Clear();

        // Choose the decode step based on the MPEG layer and the current codec type.
        switch (_state)
        {
            case Layer1DecoderState layer1
                when header.Layer == MpegLayer.Layer1:
                layer1.Decode(reader, header, _buf);
                break;
            case Layer2DecoderState layer2
                when header.Layer == MpegLayer.Layer2:
                layer2.Decode(reader, header, _buf);
                break;
            case Layer3DecoderState layer3
                when header.Layer == MpegLayer.Layer3:
                layer3.Decode(reader, header, _buf);
                break;
            default:
                throw new FormatException("mpa: invalid layer.");
        }

        _buf.Trim(packet.TrimStart, packet.TrimEnd);
    }
}