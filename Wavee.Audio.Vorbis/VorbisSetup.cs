using Wavee.Audio.Vorbis.Decoding;
using Wavee.Audio.Vorbis.Decoding.Codebooks;
using Wavee.Audio.Vorbis.Decoding.Floors;
using Wavee.Audio.Vorbis.Decoding.Map;
using Wavee.Audio.Vorbis.Mapping.Mappers;
using Wavee.Audio.Vorbis.Residues;

namespace Wavee.Audio.Vorbis;

internal class VorbisSetup
{
    public Mode[] Modes { get; set; }
    public Residue[] Residues { get; set; }
    public VorbisCodebook[] Codebooks { get; set; }
    public IFloor[] Floors { get; set; }
    public VorbisMapping[] Mappings { get; set; }
}