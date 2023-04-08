using Wavee.Audio.IO;

namespace Wavee.Audio.Vorbis.Decoding.Map;

public class VorbisMapping
{
    private VorbisMapping(List<ChannelCouple> couplings, List<byte> multiplex, List<Submap> submaps)
    {
        Couplings = couplings;
        Multiplex = multiplex;
        Submaps = submaps;
    }
    public List<ChannelCouple> Couplings { get; }
    public List<byte> Multiplex { get; }
    public List<Submap> Submaps { get; }

    public static VorbisMapping ReadMappingType0(BitReaderRtl bs,
        byte audioChannels,
        byte maxFloor,
        byte maxResidue)
    {
        var numSubMaps = 1;
        if (bs.ReadBool())
        {
            numSubMaps = (byte)(bs.ReadBitsLeq32(4) + 1);
        }

        var couplings = new List<ChannelCouple>();

        if (bs.ReadBool())
        {
            // Number of channel couplings (up-to 256).
            var couplingSteps = (ushort)(bs.ReadBitsLeq32(8) + 1);

            //Reserve space
            couplings.Capacity = couplingSteps;

            //The maximum channel number
            var maxCh = (byte)(audioChannels - 1);

            // The number of bits to read for the magnitude and angle channel numbers. Never exceeds 8.
            var couplingBits = ((uint)maxCh).ILog();
            if (couplingBits > 8)
                throw new NotSupportedException("Coupling bits exceeds 8.");

            //Read each channel coupling
            for (int i = 0; i < couplingSteps; i++)
            {
                var magnitude = (byte)bs.ReadBitsLeq32(couplingBits);
                var angle = (byte)bs.ReadBitsLeq32(couplingBits);

                if (magnitude > maxCh || angle > maxCh)
                    throw new NotSupportedException("Coupling magnitude or angle exceeds maximum channel number.");

                couplings.Add(new ChannelCouple(magnitude, angle));
            }
        }

        if (bs.ReadBitsLeq32(2) != 0)
        {
            throw new NotSupportedException("Vorbis mapping type 0 reserved bits are not zero.");
        }

        var multiplex = new List<byte>(audioChannels);

        // If the number of submaps is > 1 read the multiplex numbers from the bitstream, otherwise
        // they're all 0.
        if (numSubMaps > 1)
        {
            for (int i = 0; i < audioChannels; i++)
            {
                var mux = (byte)bs.ReadBitsLeq32(4);

                if (mux >= numSubMaps)
                    throw new NotSupportedException(
                        "Vorbis mapping type 0 multiplex number exceeds number of submaps.");

                multiplex.Add(mux);
            }
        }
        else
        {
            //Reserve space
            for (int i = 0; i < audioChannels; i++)
            {
                multiplex.Add(0);
            }
        }

        var submaps = new List<Submap>(numSubMaps);

        for (int i = 0; i < numSubMaps; i++)
        {
            //unused
            var _ = bs.ReadBitsLeq32(8);

            //the floor to use
            var floor = (byte)bs.ReadBitsLeq32(8);

            if (floor > maxFloor)
                throw new NotSupportedException("Vorbis mapping type 0 floor number exceeds maximum floor number.");

            //the residue to use
            var residue = (byte)bs.ReadBitsLeq32(8);

            if (residue > maxResidue)
                throw new NotSupportedException("Vorbis mapping type 0 residue number exceeds maximum residue number.");

            submaps.Add(new Submap(floor, residue));
        }

        var mapping = new VorbisMapping(couplings, multiplex, submaps);
        return mapping;
    }
}

public record Submap(byte Floor, byte Residue);

public record ChannelCouple(byte Magnitude, byte Angle);