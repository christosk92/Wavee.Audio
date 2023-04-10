using System.Diagnostics;
using Wavee.Audio.Audio;
using Wavee.Audio.Codecs;
using Wavee.Audio.Formats;
using Wavee.Audio.Helpers.Extensions;
using Wavee.Audio.IO;
using Wavee.Audio.Vorbis.Decoding;
using Wavee.Audio.Vorbis.Decoding.DspState;
using Wavee.Audio.Vorbis.Decoding.Floors;
using Wavee.Audio.Vorbis.Decoding.Map;
using Wavee.Audio.Vorbis.Mapping;
using Wavee.Audio.Vorbis.Mapping.Mappers;
using Wavee.Audio.Vorbis.Residues;

namespace Wavee.Audio.Vorbis;

public sealed class VorbisDecoder : IDisposable
{
    private readonly CodecParameters _params;

    private readonly IdentHeader _ident;

    private VorbisCodebook[] _codebooks;
    private IFloor[] _floors;
    private Residue[] _residues;
    private Mode[] _modes;
    private VorbisMapping[] _mappings;
    private Dsp _dsp;
    private AudioBuffer<float> _buf { get; set; }

    public VorbisDecoder(CodecParameters parameters, DecoderOptions options)
    {
        _params = parameters;
        if (parameters.Codec != VorbisMapper.CODEC_TYPE_VORBIS)
            throw new ArgumentException("Codec must be Vorbis", nameof(parameters));

        // Get the extra data (mandatory).
        var extraData = parameters.ExtraData;
        if (!extraData.HasValue)
            throw new ArgumentException("Extra data must be present", nameof(parameters));

        // The extra data contains the identification and setup headers.
        var reader = new BufReader(extraData.Value.Span);

        // Read the identification header.
        var ident = ReadIdentHeader(reader);

        // Read the setup data.
        var setup = ReadSetup(reader, ident);

        // Initialize static DSP data.
        var windows = new Windows(1 << ident.Bs0Exp, 1 << ident.Bs1Exp);

        // Initialize dynamic DSP for each channel.
        var dspChannels = Enumerable
            .Range(0, ident.NChannels)
            .Select(c => new DspChannel(ident.Bs0Exp, ident.Bs1Exp)).ToArray();

        // Map the channels
        if (!VorbisChannels.TryGetChannels(ident.NChannels, out var channels))
        {
            throw new NotSupportedException("Vorbis channel mapping is invalid.");
        }

        // Initialize the output buffer.
        var spec = new SignalSpec((int)ident.SampleRate, channels.Value);

        var imdctShort = new Imdct((1 << ident.Bs0Exp) >> 1);
        var imdctLong  = new Imdct((1 << ident.Bs1Exp) >> 1);

        // TODO: Should this be half the block size?
        //        let duration = 1u64 << ident.bs1_exp;
        var duration = 1UL << ident.Bs1Exp;

        var dsp = new Dsp(windows, dspChannels, imdctShort, imdctLong);

        _params = parameters;
        _ident = ident;
        _codebooks = setup.Codebooks;
        _floors = setup.Floors;
        _residues = setup.Residues;
        _modes = setup.Modes;
        _mappings = setup.Mappings;
        _dsp = dsp;
        _buf = new AudioBuffer<float>(duration, spec);
    }

    private VorbisSetup ReadSetup(BufReader reader, IdentHeader ident)
    {
        // The packet type must be an setup header.
        var packetType = reader.ReadByte();

        if (packetType != VorbisMapper.VORBIS_PACKET_TYPE_SETUP)
            throw new NotSupportedException("Vorbis setup header packet type is invalid.");

        // Next, the header packet signature must be correct.
        Span<byte> packetSignature = stackalloc byte[6];
        reader.ReadExact(packetSignature);
        if (!packetSignature.SequenceEqual(VorbisMapper.VORBIS_HEADER_PACKET_SIGNATURE))
            throw new NotSupportedException("Vorbis setup header packet signature is invalid.");

        // The remaining portion of the setup header packet is read bitwise.
        var bs = new BitReaderRtl(reader.ReadBufBytesAvailable());

        //Read codebooks
        var codebooks = ReadCodebooks(bs).ToArray();

        // Read time-domain transforms (placeholders in Vorbis 1).
        ReadTimeDomainTransforms(bs);

        // Read floors.
        var floors = ReadFloors(bs, ident.Bs0Exp, ident.Bs1Exp, (byte)codebooks.Length).ToArray();

        // Read residues.
        var residues = ReadResidues(bs, (byte)codebooks.Length).ToArray();

        //Read channel mappings.
        var mappings = ReadMappings(bs, ident.NChannels, (byte)floors.Length, (byte)residues.Length)
            .ToArray();

        //Read modes
        var modes = Utils.ReadModes(bs);

        //Framing flag must be set
        if (!bs.ReadBool())
            throw new NotSupportedException("Vorbis setup header framing flag is invalid.");

        //check bits left

        return new VorbisSetup
        {
            Codebooks = codebooks,
            Floors = floors,
            Residues = residues,
            Mappings = mappings,
            Modes = modes
        };
    }

    private static IEnumerable<VorbisMapping> ReadMappings(BitReaderRtl bs, byte audioChannels, byte maxFloor,
        byte maxResidue)
    {
        var count = bs.ReadBitsLeq32(6) + 1;
        for (int i = 0; i < count; ++i)
        {
            yield return ReadMapping(bs, audioChannels, maxFloor, maxResidue);
        }
    }

    private static VorbisMapping ReadMapping(BitReaderRtl bs, byte audioChannels, byte maxFloor, byte maxResidue)
    {
        var mappingType = bs.ReadBitsLeq32(16);
        return mappingType switch
        {
            0 => VorbisMapping.ReadMappingType0(bs, audioChannels, maxFloor, maxResidue),
            _ => throw new NotSupportedException("Vorbis mapping type is invalid.")
        };
    }

    private IEnumerable<Residue> ReadResidues(BitReaderRtl bs, byte maxCodebook)
    {
        var count = bs.ReadBitsLeq32(6) + 1;
        for (int i = 0; i < count; ++i)
        {
            yield return ReadResidue(bs, maxCodebook);
        }
    }

    private static Residue ReadResidue(BitReaderRtl bs, byte maxCodebook)
    {
        var residueType = (ushort)bs.ReadBitsLeq32(16);
        return residueType switch
        {
            0 or 1 or 2 => Residue.Read(bs, residueType, maxCodebook),
            _ => throw new NotSupportedException("Vorbis residue type is invalid.")
        };
    }

    private IEnumerable<IFloor> ReadFloors(BitReaderRtl bs, byte identBs0Exp, byte identBs1Exp, byte max_codebook)
    {
        var count = bs.ReadBitsLeq32(6) + 1;
        for (int i = 0; i < count; ++i)
        {
            yield return ReadFloor(bs, identBs0Exp, identBs1Exp, max_codebook);
        }
    }

    private static IFloor ReadFloor(BitReaderRtl bs, byte identBs0Exp, byte identBs1Exp, byte maxCodebook)
    {
        var floorType = bs.ReadBitsLeq32(16);

        return floorType switch
        {
            0 => Floor0.Read(bs, identBs0Exp, identBs1Exp, maxCodebook),
            1 => Floor1.Read(bs, identBs0Exp, identBs1Exp, maxCodebook),
            _ => throw new NotSupportedException("Vorbis floor type is invalid.")
        };
    }

    private static void ReadTimeDomainTransforms(BitReaderRtl bs)
    {
        var count = bs.ReadBitsLeq32(6) + 1;

        for (int i = 0; i < count; ++i)
        {
            // All these values are placeholders and must be 0.
            var dim = bs.ReadBitsLeq32(16);
            if (dim != 0)
                throw new NotSupportedException("Vorbis time domain transform dimension is invalid.");
        }
    }

    private static IEnumerable<VorbisCodebook> ReadCodebooks(BitReaderRtl bs)
    {
        var count = bs.ReadBitsLeq32(8) + 1;
        for (int i = 0; i < count; ++i)
        {
            yield return VorbisCodebook.Read(bs);
        }
    }

    private static IdentHeader ReadIdentHeader(BufReader bufReader)
    {
        // The packet type must be an identification header.
        var packetType = bufReader.ReadByte();
        if (packetType != VorbisMapper.VORBIS_PACKET_TYPE_IDENTIFICATION)
        {
            // header = default;
            // Debug.WriteLine("Vorbis identification header packet type is invalid.");
            // return false;
            throw new NotSupportedException("Vorbis identification header packet type is invalid.");
        }

        // Next, the header packet signature must be correct.
        Span<byte> packetSignature = stackalloc byte[6];
        bufReader.ReadExact(packetSignature);

        if (!packetSignature.SequenceEqual(VorbisMapper.VORBIS_HEADER_PACKET_SIGNATURE))
        {
            throw new NotSupportedException("Vorbis identification header packet signature is invalid.");
        }

        //Next the vorbis version must be 0.
        var vorbisVersion = bufReader.ReadUInt();

        if (vorbisVersion != 0)
        {
            throw new NotSupportedException("Vorbis identification header packet version is invalid.");
        }

        // Next, the number of channels must be valid.
        var nChannels = bufReader.ReadByte();
        if (nChannels == 0)
        {
            throw new NotSupportedException("Vorbis identification header packet channel count is invalid.");
        }

        var sampleRate = bufReader.ReadUInt();
        if (sampleRate == 0)
        {
            throw new NotSupportedException("Vorbis identification header packet sample rate is invalid.");
        }

        //read bitrate range
        var _bitrateMax = bufReader.ReadUInt();
        var _bitrateNominal = bufReader.ReadUInt();
        var _bitrateMin = bufReader.ReadUInt();

        // Next, blocksize_0 and blocksize_1 are packed into a single byte.
        var blockSizes = bufReader.ReadByte();

        byte bs0Exp = (byte)((blockSizes & 0x0F) >> 0);
        byte bs1Exp = (byte)((blockSizes & 0xF0) >> 4);

        // The block sizes must not exceed the bounds.
        if (bs0Exp < VorbisMapper.VORBIS_BLOCKSIZE_MIN || bs0Exp > VorbisMapper.VORBIS_BLOCKSIZE_MAX ||
            bs1Exp < VorbisMapper.VORBIS_BLOCKSIZE_MIN || bs1Exp > VorbisMapper.VORBIS_BLOCKSIZE_MAX)
        {
            throw new NotSupportedException("Vorbis identification header packet block sizes are invalid.");
        }

        //blocksize_0 must be >= blocksize_1
        if (bs0Exp > bs1Exp)
        {
            throw new NotSupportedException("Vorbis identification header packet block sizes are invalid.");
        }

        //Framing flag must be set
        var framingFlag = bufReader.ReadByte();
        if (framingFlag != 0x1)
        {
            throw new NotSupportedException("Vorbis framing flag is not supported.");
        }

        return new IdentHeader(nChannels, sampleRate, bs0Exp, bs1Exp);
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
        var bs = new BitReaderRtl(packet.Data);

        // Section 4.3.1 - Packet Type, Mode, and Window Decode

        // First bit must be 0 to indicate audio packet.
        if (bs.ReadBool())
        {
            throw new NotSupportedException("Vorbis audio packet type is invalid.");
        }

        var numNodes = _modes.Length - 1;

        var modeNumber = bs.ReadBitsLeq32(((uint)numNodes).ILog());

        if (modeNumber >= _modes.Length)
        {
            throw new NotSupportedException("Vorbis audio packet mode number is invalid.");
        }

        var mode = _modes[modeNumber];
        var mapping = _mappings[mode.Mapping];

        (byte bsExp, Imdct imdct) = (_ident.Bs0Exp, _dsp.ImdctShort);

        if (mode.BlockFlag)
        {
            // This packet (block) uses a long window. Do not use the window flags since they may
            // be wrong.
            var _prevWindowFlag = bs.ReadBool();
            var _nextWindowFlag = bs.ReadBool();

            bsExp = _ident.Bs1Exp;
            imdct = _dsp.ImdctLong;
        }

        // Block, and half-block size
        var n = 1 << bsExp;
        var n2 = n >> 1;

        // Section 4.3.2 - Floor Curve Decode

        // Read the floors from the packet. There is one floor per audio channel. Each mapping will
        // have one multiplex (submap number) per audio channel. Therefore, iterate over all
        // muxes in the mapping, and read the floor.
        foreach (var (submapNum, ch) in mapping.Multiplex.Zip(_dsp.Channels))
        {
            var submap = mapping.Submaps[submapNum];
            var floor = _floors[submap.Floor];

            // Read the floor from the bitstream.
            floor.ReadChannel(bs, _codebooks);

            ch.DoNotDecode = floor.IsUnused;

            if (!ch.DoNotDecode)
            {
                // Since the same floor can be used by multiple channels and thus overwrite the
                // data just read from the bitstream, synthesize the floor curve for this channel
                // now and save it for audio synthesis later.
                floor.Synthesis(bsExp, ch.Floor);
            }
            else
            {
                // If the channel is unused, zero the floor vector.
                for (int i = 0; i < n2; ++i)
                {
                    ch.Floor[i] = 0;
                }
            }
        }

        // Section 4.3.3 - Non-zero Vector Propagate

        // If within a pair of coupled channels, one channel has an unused floor (do_not_decode
        // is true for that channel), but the other channel is used, then both channels must have
        // do_not_decode unset.
        foreach (var couple in mapping.Couplings)
        {
            var magnitudeChIdx = couple.Magnitude;
            var angleChIdx = couple.Angle;

            if (_dsp.Channels[magnitudeChIdx].DoNotDecode
                != _dsp.Channels[angleChIdx].DoNotDecode)
            {
                _dsp.Channels[magnitudeChIdx].DoNotDecode = false;
                _dsp.Channels[angleChIdx].DoNotDecode = false;
            }
        }

        // Section 4.3.4 - Residue Decode
        for (int submapIdx = 0; submapIdx < mapping.Submaps.Count; submapIdx++)
        {
            var submap = mapping.Submaps[submapIdx];
            var residueChannels = new BitSet256();

            // Find the channels using this submap.
            for (int chIdx = 0; chIdx < mapping.Multiplex.Count; chIdx++)
            {
                if (mapping.Multiplex[chIdx] == submapIdx)
                {
                    residueChannels.Set(chIdx);
                }
            }

            var residue = _residues[submap.Residue];

            residue
                .ReadResidue(
                    bs,
                    bsExp,
                    _codebooks,
                    residueChannels,
                    _dsp.Channels
                );
        }

        // Section 4.3.5 - Inverse Coupling
        foreach (var coupling in mapping.Couplings)
        {
            Debug.Assert(coupling.Magnitude != coupling.Angle);

            // Get mutable reference to each channel in the pair.
            DspChannel magnitudeCh, angleCh;
            if (coupling.Magnitude < coupling.Angle)
            {
                // Magnitude channel index < angle channel index.
                magnitudeCh = _dsp.Channels[coupling.Magnitude];
                angleCh = _dsp.Channels[coupling.Angle];
            }
            else
            {
                // Angle channel index < magnitude channel index.
                magnitudeCh = _dsp.Channels[coupling.Angle];
                angleCh = _dsp.Channels[coupling.Magnitude];
            }

            for (int i = 0; i < n2; i++)
            {
                float m = magnitudeCh.Residue[i];
                float a = angleCh.Residue[i];
                float newM, newA;

                if (m > 0.0f)
                {
                    if (a > 0.0f)
                    {
                        newM = m;
                        newA = m - a;
                    }
                    else
                    {
                        newM = m + a;
                        newA = m;
                    }
                }
                else
                {
                    if (a > 0.0f)
                    {
                        newM = m;
                        newA = m + a;
                    }
                    else
                    {
                        newM = m - a;
                        newA = m;
                    }
                }

                magnitudeCh.Residue[i] = newM;
                angleCh.Residue[i] = newA;
            }
        }

        // Section 4.3.6 - Dot Product
        foreach (DspChannel channel in _dsp.Channels)
        {
            // If the channel is marked as do not decode, the floor vector is all 0. Therefore the
            // dot product will be 0.
            if (channel.DoNotDecode)
            {
                continue;
            }

            for (int i = 0; i < n2; i++)
            {
                channel.Floor[i] *= channel.Residue[i];
            }
        }

        // Combined Section 4.3.7 and 4.3.8 - Inverse MDCT and Overlap-add (Synthesis)
        _buf.Clear();

        // Calculate the output length and reserve space in the output buffer. If there was no
        // previous packet, then return an empty audio buffer since the decoder will need another
        // packet before being able to produce audio.
        if (_dsp.LappingState is not null)
        {
            // The previous block size.
            var prevBlockSize =
                _dsp.LappingState.PrevBlockFlag ? (1 << _ident.Bs1Exp) : (1 << _ident.Bs0Exp);

            var renderLen = (prevBlockSize + n) / 4;
            _buf.RenderReserve(renderLen);
        }

        // Render all the audio channels.
        for (int i = 0; i < _dsp.Channels.Length; i++)
        {
            var mapped = MapVorbisChannel(_ident.NChannels, i);
            var channel = _dsp.Channels[i];
            channel.Synth(
                mode.BlockFlag,
                _dsp.LappingState,
                _dsp.Windows,
                imdct,
                _buf.ChanMut(mapped)
            );
        }

        // Trim
        _buf.Trim(packet.TrimStart, packet.TrimEnd);

        // Save the new lapping state.
        _dsp.LappingState = new LappingState
        {
            PrevBlockFlag = mode.BlockFlag
        };
    }

    private static int MapVorbisChannel(int numChannels, int ch)
    {
        // This pre-condition should always be true.
        Debug.Assert(ch < numChannels);

        int mappedCh;
        switch (numChannels)
        {
            case 1:
                mappedCh = new int[] { 0 }[ch]; // FL
                break;
            case 2:
                mappedCh = new int[] { 0, 1 }[ch]; // FL, FR
                break;
            case 3:
                mappedCh = new int[] { 0, 2, 1 }[ch]; // FL, FC, FR
                break;
            case 4:
                mappedCh = new int[] { 0, 1, 2, 3 }[ch]; // FL, FR, RL, RR
                break;
            case 5:
                mappedCh = new int[] { 0, 2, 1, 3, 4 }[ch]; // FL, FC, FR, RL, RR
                break;
            case 6:
                mappedCh = new int[] { 0, 2, 1, 4, 5, 3 }[ch]; // FL, FC, FR, RL, RR, LFE
                break;
            case 7:
                mappedCh = new int[] { 0, 2, 1, 5, 6, 4, 3 }[ch]; // FL, FC, FR, SL, SR, RC, LFE
                break;
            case 8:
                mappedCh = new int[] { 0, 2, 1, 6, 7, 4, 5, 3 }[ch]; // FL, FC, FR, SL, SR, RL, RR, LFE
                break;
            default:
                return ch;
        }

        return mappedCh;
    }


    public void Dispose()
    {
    }
}

public class Windows
{
    public float[] Short { get; }
    public float[] Long { get; }

    public Windows(int blockSize0, int blockSize1)
    {
        Short = GenerateWindow(blockSize0);
        Long = GenerateWindow(blockSize1);
    }

    /// <summary>
    /// For a given window size, generates the curve of the left-half of the window
    /// </summary>
    /// <param name="blockSize0"></param>
    /// <returns></returns>
    /// <exception cref="NotImplementedException"></exception>
    private static float[] GenerateWindow(int bs)
    {
        int len = bs / 2;
        double denom = Convert.ToDouble(len);

        var slope = new float[len];

        for (int i = 0; i < len; i++)
        {
            double num = Convert.ToDouble(i) + 0.5;
            double frac = Math.PI / 2 * (num / denom);
            slope[i] = (float)Math.Sin(Math.PI / 2 * Math.Pow(Math.Sin(frac), 2));
        }

        return slope;
    }
}