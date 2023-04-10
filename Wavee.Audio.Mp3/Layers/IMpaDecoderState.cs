using Wavee.Audio.Audio;
using Wavee.Audio.IO;
using Wavee.Audio.Mp3.Frame;

namespace Wavee.Audio.Mp3.Layers;

internal interface IMpaDecoderState
{
    void Decode(
        BufReader reader,
        FrameHeader header,
        AudioBuffer<float> output);
}