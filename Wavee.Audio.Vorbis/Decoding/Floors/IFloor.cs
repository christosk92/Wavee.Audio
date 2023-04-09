using Wavee.Audio.IO;
using Wavee.Audio.Vorbis.Decoding.DspState;

namespace Wavee.Audio.Vorbis.Decoding.Floors;

internal interface IFloor
{
    void ReadChannel(BitReaderRtl bs, VorbisCodebook[] ch);
    bool IsUnused { get; }
    void Synthesis(byte bsExp, float[] floor);
}