using System.Diagnostics;
using Wavee.Audio.Codecs;
using Wavee.Audio.Helpers.Extensions;
using Wavee.Audio.IO;
using Wavee.Audio.Meta;
using Wavee.Audio.Vorbis.Exception;
using Wavee.Audio.Vorbis.Meta;

namespace Wavee.Audio.Vorbis.Mapping.Mappers;

internal sealed class VorbisMapper : IMapper
{
    internal const byte VORBIS_PACKET_TYPE_IDENTIFICATION = 1;
    private const byte VORBIS_PACKET_TYPE_COMMENT = 3;
    internal const byte VORBIS_PACKET_TYPE_SETUP = 5;

    private const int IdentificationHeaderSize = 30;
    internal const byte VORBIS_BLOCKSIZE_MIN = 6;
    internal const byte VORBIS_BLOCKSIZE_MAX = 13;

    internal static CodecType CODEC_TYPE_VORBIS = new CodecType(0x1000);
    internal static byte[] VORBIS_HEADER_PACKET_SIGNATURE = "vorbis"u8.ToArray();


    private CodecParameters _codecParameters;
    private readonly IdentHeader _identHeader;

    private VorbisPacketParser? _parser;
    private bool _hasSetupHeader;

    private VorbisMapper(CodecParameters codecParameters,
        IdentHeader identHeader,
        VorbisPacketParser? parser,
        bool hasSetupHeader)
    {
        _codecParameters = codecParameters;
        _identHeader = identHeader;
        _parser = parser;
        _hasSetupHeader = hasSetupHeader;
    }


    public static bool TryDetect(ReadOnlySpan<byte> pkt, out VorbisMapper mapper)
    {
        // The identification header packet must be the correct size.
        if (pkt.Length != IdentificationHeaderSize)
        {
            mapper = null;
            return false;
        }

        // Read the identification header. Any errors cause detection to fail.
        if (!ReadIdentHeader(pkt, out var identHeader))
        {
            mapper = null;
            return false;
        }

        // Populate the codec parameters
        var codecParameters = new CodecParameters();

        codecParameters = codecParameters with
        {
            Codec = CODEC_TYPE_VORBIS,
            SampleRate = identHeader.SampleRate,
            TimeBase = new TimeBase(1, identHeader.SampleRate),
            ExtraData = pkt.ToArray()
        };

        if (VorbisChannels.TryGetChannels(identHeader.NChannels, out var channels))
            codecParameters = codecParameters with { Channels = channels };

        // Instantiate the Vorbis mapper.
        mapper = new VorbisMapper(codecParameters, identHeader, null, false);
        return true;
    }

    private static bool ReadIdentHeader(ReadOnlySpan<byte> pkt, out IdentHeader header)
    {
        var bufReader = new BufReader(pkt);
        // The packet type must be an identification header.
        var packetType = bufReader.ReadByte();
        if (packetType != VORBIS_PACKET_TYPE_IDENTIFICATION)
        {
            header = default;
            Debug.WriteLine("Vorbis identification header packet type is invalid.");
            return false;
        }

        // Next, the header packet signature must be correct.
        Span<byte> packetSignature = stackalloc byte[6];
        bufReader.ReadExact(packetSignature);

        if (!packetSignature.SequenceEqual(VORBIS_HEADER_PACKET_SIGNATURE))
        {
            header = default;
            Debug.WriteLine("Vorbis identification header packet signature is invalid.");
            return false;
        }

        //Next the vorbis version must be 0.
        var vorbisVersion = bufReader.ReadUInt();

        if (vorbisVersion != 0)
        {
            header = default;
            Debug.WriteLine("Vorbis identification header packet version is invalid.");
            return false;
        }

        // Next, the number of channels must be valid.
        var nChannels = bufReader.ReadByte();
        if (nChannels == 0)
        {
            header = default;
            Debug.WriteLine("Vorbis identification header packet channel count is invalid.");
            return false;
        }

        var sampleRate = bufReader.ReadUInt();
        if (sampleRate == 0)
        {
            header = default;
            Debug.WriteLine("Vorbis identification header packet sample rate is invalid.");
            return false;
        }

        //read bitrate range
        var bitrateMax = bufReader.ReadUInt();
        var bitrateNominal = bufReader.ReadUInt();
        var bitrateMin = bufReader.ReadUInt();

        // Next, blocksize_0 and blocksize_1 are packed into a single byte.
        var blockSizes = bufReader.ReadByte();

        byte bs0Exp = (byte)((blockSizes & 0x0F) >> 0);
        byte bs1Exp = (byte)((blockSizes & 0xF0) >> 4);

        // The block sizes must not exceed the bounds.
        if (bs0Exp < VORBIS_BLOCKSIZE_MIN || bs0Exp > VORBIS_BLOCKSIZE_MAX ||
            bs1Exp < VORBIS_BLOCKSIZE_MIN || bs1Exp > VORBIS_BLOCKSIZE_MAX)
        {
            header = default;
            Debug.WriteLine("Vorbis identification header packet block sizes are invalid.");
            return false;
        }

        //blocksize_0 must be >= blocksize_1
        if (bs0Exp > bs1Exp)
        {
            header = default;
            Debug.WriteLine("Vorbis identification header packet block sizes are invalid.");
            return false;
        }

        //Framing flag must be set
        var framingFlag = bufReader.ReadByte();
        if (framingFlag != 0x1)
        {
            header = default;
            Debug.WriteLine("Vorbis identification header packet framing flag is invalid.");
            return false;
        }

        header = new IdentHeader(nChannels, sampleRate, bs0Exp, bs1Exp);
        return true;
    }

    public string Name => "Vorbis";
    public bool IsReady => _hasSetupHeader;

    public IMapResult MapPacket(ReadOnlySpan<byte> data)
    {
        var reader = new BufReader(data);

        // All Vorbis packets indicate the packet type in the first byte.
        var packetType = reader.ReadByte();

        // An even numbered packet type is an audio packet.
        if ((packetType & 1) == 0)
        {
            ulong dur = 0;
            if (_parser is not null)
            {
                dur = _parser.ParseNextPacketDur(data);
            }

            return new IMapResult.StreamData(dur);
        }
        else
        {
            // Odd numbered packet types are header packets.
            Span<byte> sig = new byte[6];
            reader.ReadExact(sig);

            // Check if the presumed header packet has the common header packet signature.
            if (!sig.SequenceEqual(VORBIS_HEADER_PACKET_SIGNATURE))
            {
                return new IMapResult.ErrorData(new OggDecodeException(DecodeErrorType.HeaderSignatureInvalid));
            }

            // Handle each header packet type specifically.
            switch (packetType)
            {
                case VORBIS_PACKET_TYPE_COMMENT:
                    var builder = new AudioMetadataBuilder();

                    VorbisMeta.ReadCommentNoFraming(reader, ref builder);

                    return new IMapResult.SideData(
                        new ISideData.MetadataSideData(builder.Metadata)
                    );
                case VORBIS_PACKET_TYPE_SETUP:
                    // Append the setup headers to the extra data.
                    _codecParameters.ExtraData ??= Array.Empty<byte>();
                    var extraData = _codecParameters.ExtraData.Value;
                    var packetData = data;
                    Memory<byte> combinedData = new byte[extraData.Length + packetData.Length];

                    extraData.CopyTo(combinedData);
                    packetData.CopyTo(combinedData[extraData.Length..].Span);

                    _codecParameters.ExtraData = combinedData;

                    // Try to read the setup header.
                    if (ReadSetup(new BufReader(data), _identHeader, out var modes))
                    {
                        var numModes = modes.Length;
                        ulong modesBlockFlags = 0;

                        for (int i = 0; i < modes.Length; i++)
                        {
                            if (modes[i].BlockFlag)
                            {
                                modesBlockFlags |= (ulong)1 << i;
                            }
                        }

                        var parser = new VorbisPacketParser(
                            _identHeader.Bs0Exp,
                            _identHeader.Bs1Exp,
                            (byte)numModes,
                            modesBlockFlags
                        );
                        _parser = parser;
                    }

                    _hasSetupHeader = true;

                    return new IMapResult.SetupData();
                    break;
                default:
                    Debug.WriteLine($"Packet type {packetType} is invalid.");
                    return new IMapResult.ErrorData(new OggDecodeException(DecodeErrorType.HeaderPacketTypeInvalid));
                    break;
            }
        }
    }

    public bool TryMakeParser(out VorbisPacketParser? o)
    {
        if (_parser is not null)
        {
            var parser = new VorbisPacketParser(
                _parser.Bs0Exp,
                _parser.Bs1Exp,
                _parser.NumModes,
                _parser.ModesBlockFlags
            );
            o = parser;
            return true;
        }

        o = default;
        return false;
    }

    public CodecParameters CodecParams() => _codecParameters;

    public void UpdateCodecParams(CodecParameters o)
    {
        _codecParameters = o;
    }

    public void Reset()
    {
        if (_parser is not null) _parser.Reset();
    }

    private bool ReadSetup(BufReader reader, IdentHeader identHeader, out Mode[] o)
    {
        // The packet type must be an setup header.
        var packetType = reader.ReadByte();

        if (packetType != VORBIS_PACKET_TYPE_SETUP)
        {
            o = default;
            Debug.WriteLine("Vorbis setup header packet type is invalid.");
            return false;
        }

        // Next, the header packet signature must be correct.
        Span<byte> packetSignature = stackalloc byte[6];
        reader.ReadExact(packetSignature);

        if (!packetSignature.SequenceEqual(VORBIS_HEADER_PACKET_SIGNATURE))
        {
            o = default;
            Debug.WriteLine("Vorbis setup header packet signature is invalid.");
            return false;
        }

        // The remaining portion of the setup header packet is read bitwise.
        var bs = new BitReaderRtl(reader.ReadBufBytesAvailable());

        //Skip the codebooks
        SkipCodebooks(bs);

        //Skip the time domain transforms
        SkipTimeDomainTransforms(bs);

        //Skip the floors
        SkipFloors(bs);

        //Skip the residues
        SkipResidues(bs);

        //Skip the mappings
        SkipMappings(bs, _identHeader.NChannels);

        //Read the modes
        o = Utils.ReadModes(bs);

        if (!bs.ReadBool())
        {
            o = default;
            Debug.WriteLine("Vorbis setup header packet framing flag is invalid.");
            return false;
        }

        return true;
    }


    private static void SkipCodebooks(BitReaderRtl bs)
    {
        static void SkipCodebook(BitReaderRtl bs)
        {
            // Verify codebook synchronization word.
            var sync = bs.ReadBitsLeq32(24);

            if (sync != 0x564342)
            {
                Debug.WriteLine("Vorbis codebook synchronization word is invalid.");
                return;
            }

            // Read the dimensions.
            var dimensions = (ushort)bs.ReadBitsLeq32(16);
            var entries = (uint)bs.ReadBitsLeq32(24);
            var isLengthOrdered = bs.ReadBool();

            if (!isLengthOrdered)
            {
                // Codeword list is not length ordered.
                var isSparse = bs.ReadBool();

                if (isSparse)
                {
                    // Sparsely packed codeword entry list.
                    for (int i = 0; i < entries; i++)
                    {
                        if (bs.ReadBool())
                        {
                            bs.ReadBitsLeq32(5);
                        }
                    }
                }
                else
                {
                    bs.IgnoreBits(entries * 5);
                }
            }
            else
            {
                // Codeword list is length ordered.
                uint curEntry = 0;
                var _ = bs.ReadBitsLeq32(5) + 1;

                while (true)
                {
                    uint numBits = 0;
                    if (entries > curEntry)
                    {
                        numBits = (entries - curEntry).ILog();
                    }

                    var num = (uint)bs.ReadBitsLeq32(numBits);

                    curEntry += num;

                    if (curEntry > entries)
                    {
                        Debug.WriteLine("Vorbis codebook entry count is invalid.");
                        return;
                    }

                    if (curEntry == entries)
                    {
                        break;
                    }
                }
            }

            // Read and unpack vector quantization (VQ) lookup table.
            var lookupType = bs.ReadBitsLeq32(4);

            switch (lookupType & 0xf)
            {
                case 0:
                    // No lookup table.
                    break;
                case 1:
                case 2:
                    var _minVal = bs.ReadBitsLeq32(32);
                    var _deltaVal = bs.ReadBitsLeq32(32);
                    var _valueBits = bs.ReadBitsLeq32(4) + 1;
                    var _sequenceP = bs.ReadBool();

                    // Lookup type is either 1 or 2 as per outer match.
                    var lookupValues = lookupType switch
                    {
                        1 => Utils.lookup1_values(entries, dimensions),
                        2 => entries * dimensions,
                        _ => throw new InvalidOperationException("Lookup type is invalid.")
                    };

                    // Multiplicands
                    bs.IgnoreBits((uint)(lookupValues * _valueBits));
                    break;
                default:
                    Debug.WriteLine("Vorbis codebook lookup type is invalid.");
                    throw new OggDecodeException("Vorbis codebook lookup type is invalid.");
            }
        }

        var count = bs.ReadBitsLeq32(8) + 1;
        for (int i = 0; i < count; i++)
        {
            SkipCodebook(bs);
        }
    }

    private static void SkipTimeDomainTransforms(BitReaderRtl bs)
    {
        var count = bs.ReadBitsLeq32(6) + 1;
        for (int i = 0; i < count; i++)
        {
            var r = bs.ReadBitsLeq32(16);
            if (r != 0)
            {
                Debug.WriteLine("Vorbis time domain transform is invalid.");
                throw new OggDecodeException("Vorbis time domain transform is invalid.");
            }
        }
    }

    private static void SkipFloors(BitReaderRtl bs)
    {
        static void SkipFloor(BitReaderRtl bs)
        {
            var floorType = bs.ReadBitsLeq32(16);
            _ = floorType switch
            {
                0 => SkipFloor0Setup(bs),
                1 => SkipFloor1Setup(bs),
                _ => throw new OggDecodeException("Vorbis floor type is invalid.")
            };
        }

        var count = bs.ReadBitsLeq32(6) + 1;

        for (int i = 0; i < count; i++)
        {
            SkipFloor(bs);
        }
    }

    private static bool SkipFloor0Setup(BitReaderRtl bs)
    {
        // floor0_order
        // floor0_rate
        // floor0_bark_map_size
        // floor0_amplitude_bits
        // floor0_amplitude_offset
        bs.IgnoreBits(8 + 16 + 16 + 6 + 8);
        var numberOfBooks = bs.ReadBitsLeq32(4) + 1;
        bs.IgnoreBits((uint)(numberOfBooks * 8));
        return true;
    }

    private static bool SkipFloor1Setup(BitReaderRtl bs)
    {
        // The number of partitions. 5-bit value, 0..31 range.
        var partitions = bs.ReadBitsLeq32(5);

        // Parition list of up-to 32 partitions (floor1_partitions), with each partition indicating
        // a 4-bit class (0..16) identifier.
        Span<byte> partitionClassList = new byte[32];
        Span<byte> classesDimensions = new byte[16];

        if (partitions > 0)
        {
            byte maxClasses = 0; // 4-bits, 0..15

            for (int i = 0; i < partitions; i++)
            {
                var partitionClass = (byte)bs.ReadBitsLeq32(4);
                partitionClassList[i] = (byte)partitionClass;
                maxClasses = Math.Max(maxClasses, partitionClass);
            }

            int numClasses = 1 + maxClasses;

            for (int i = 0; i < numClasses; i++)
            {
                classesDimensions[i] = (byte)(bs.ReadBitsLeq32(3) + 1);
                var classSubclasses = bs.ReadBitsLeq32(2);

                if (classSubclasses != 0)
                {
                    var _mainBook = bs.ReadBitsLeq32(8);
                }

                var numSubClasses = 1 << classSubclasses;

                //sub-class books
                bs.IgnoreBits((uint)(numSubClasses * 8));
            }
        }

        // floor1_multiplier
        var _multiplier = bs.ReadBitsLeq32(2);

        var rangeBits = bs.ReadBitsLeq32(4);

        for (int i = 0; i < partitions; i++)
        {
            var classIdx = partitionClassList[i];
            var classDimensions = (uint)classesDimensions[classIdx];
            // TODO? No more than 65 elements are allowed.
            bs.IgnoreBits((uint)(classDimensions * rangeBits));
        }

        return true;
    }

    private static void SkipResidues(BitReaderRtl bs)
    {
        static void SkipResidueSetup(BitReaderRtl bs)
        {
            // residue_begin
            // residue_end
            // residue_partition_size
            bs.IgnoreBits(24 + 24 + 24);
            var classifications = (byte)(bs.ReadBitsLeq32(6) + 1);

            //Residue classbook
            bs.IgnoreBits(8);

            var numCodebooks = 0;

            for (int i = 0; i < classifications; i++)
            {
                var lowbits = (byte)bs.ReadBitsLeq32(3);
                byte highBits = 0;
                if (bs.ReadBool())
                {
                    highBits = (byte)bs.ReadBitsLeq32(5);
                }

                var isUsed = (highBits << 3) | lowbits;
                numCodebooks += isUsed.CountOnes();
            }

            bs.IgnoreBits((uint)(numCodebooks * 8));
        }

        var count = bs.ReadBitsLeq32(6) + 1;
        for (int i = 0; i < count; i++)
        {
            var _residueType = bs.ReadBitsLeq32(16);
            SkipResidueSetup(bs);
        }
    }

    private static void SkipMappings(BitReaderRtl bs, byte audioChannels)
    {
        static void SkipMappingSetup(BitReaderRtl bs, byte audioChannels)
        {
            var mappingType = bs.ReadBitsLeq32(16);
            _ = mappingType switch
            {
                0 => SkipMapping0Setup(bs, audioChannels),
                _ => throw new OggDecodeException("Vorbis mapping type is invalid.")
            };
        }

        var count = bs.ReadBitsLeq32(6) + 1;
        for (int i = 0; i < count; i++)
        {
            SkipMappingSetup(bs, audioChannels);
        }
    }

    private static bool SkipMapping0Setup(BitReaderRtl bs, byte audioChannels)
    {
        int numSubmaps = bs.ReadBool() ? bs.ReadBitsLeq32(4) + 1 : 1;

        if (bs.ReadBool())
        {
            // Number of channel couplings (up-to 256).
            int couplingSteps = bs.ReadBitsLeq32(8) + 1;

            // The maximum channel number.
            int maxCh = audioChannels - 1;

            // The number of bits to read for the magnitude and angle channel numbers. Never exceeds 8.
            var couplingBits = ((uint)maxCh).ILog();
            Debug.Assert(couplingBits <= 8);

            // Read each channel coupling.
            for (int i = 0; i < couplingSteps; i++)
            {
                int magnitudeCh = bs.ReadBitsLeq32(couplingBits);
                int angleCh = bs.ReadBitsLeq32(couplingBits);
            }
        }

        if (bs.ReadBitsLeq32(2) != 0)
        {
            throw new InvalidOperationException("ogg (vorbis): reserved mapping bits non-zero");
        }

        // If the number of submaps is > 1 read the multiplex numbers from the bitstream, otherwise
        // they're all 0.
        if (numSubmaps > 1)
        {
            // Mux to use per channel.
            bs.IgnoreBits((uint)(audioChannels * 4));
        }

        // Reserved, floor, and residue to use per submap.
        bs.IgnoreBits((uint)(numSubmaps * (8 + 8 + 8)));
        return true;
    }
}

internal record Mode(bool BlockFlag, byte Mapping);

internal readonly record struct IdentHeader(byte NChannels, uint SampleRate, byte Bs0Exp, byte Bs1Exp);