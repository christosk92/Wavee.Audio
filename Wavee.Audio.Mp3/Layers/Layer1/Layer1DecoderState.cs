using Wavee.Audio.Audio;
using Wavee.Audio.IO;
using Wavee.Audio.Mp3.Frame;

namespace Wavee.Audio.Mp3.Layers.Layer1;

internal sealed class Layer1DecoderState : IMpaDecoderState
{
    public void Decode(BufReader reader, FrameHeader header, AudioBuffer<float> output)
    {
        throw new NotImplementedException();
    }
}