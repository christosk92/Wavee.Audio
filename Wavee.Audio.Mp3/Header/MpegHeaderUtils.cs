using Wavee.Audio.Helpers.Extensions;
using Wavee.Audio.IO;

namespace Wavee.Audio.Mp3.Header;

internal static class MpegHeaderUtils
{
    public static bool TryReadFrameHeaderWordNoSync<T>
        (this T reader, out uint result) where T : IReadBytes
    {
        try
        {
            result = reader.ReadUIntBE();
            return true;
        }
        catch (Exception)
        {
            result = 0;
            return false;
        }
    }

    public static bool IsSyncWord(uint sync)
    {
        return (sync & 0xFFE00000) == 0xFFE00000;
    }

    public static uint SyncFrame(MediaSourceStream reader)
    {
        uint sync = 0;

        while (true)
        {
            // Synchronize stream to the next frame using the sync word. The MPEG audio frame header
            // always starts at a byte boundary with 0xffe (11 consecutive 1 bits.) if supporting up to
            // MPEG version 2.5.
            while (!IsSyncWord(sync))
            {
                sync = (sync << 8) | (uint)reader.ReadByte();
            }

            // Random data can look like a sync word. Do a quick check to increase confidence that
            // this is may be the start of a frame.
            if (CheckHeader(sync))
                break;

            sync = (sync << 8) | (uint)reader.ReadByte();
        }

        return sync;
    }

    /// <summary>
    /// Quickly check if a header sync word may be valid.
    /// </summary>
    /// <param name="sync"></param>
    /// <returns></returns>
    /// <exception cref="NotImplementedException"></exception>
    private static bool CheckHeader(uint header)
    {
        //Version (0x1 is not allowed)
        // Layer (0x0 is not allowed).
        // BItrate (0xf is not allowed)
        // Sampling rate (0x3 is not allowed)
        //Emphasis (0x2 is not allowed)

        if (((header >> 19) & 0x3) == 0x1) return false;
        if (((header >> 17) & 0x3) == 0x0) return false;
        if (((header >> 12) & 0xf) == 0xf) return false;
        if (((header >> 10) & 0x3) == 0x3) return false;
        if ((header & 0x3) == 0x2) return false;
        
        return true;
    }
}