using Wavee.Audio.IO;

namespace Wavee.Audio.Vorbis.Exception;

public static class IoHelper
{
    public static bool TryReadBool(this BitReaderRtl reader, out bool value)
    {
        try
        {
            value = reader.ReadBool();
            return true;
        }
        catch (IOException e)
        {
            // If the error is an end-of-stream error, return false without an error
            if (e.InnerException is EndOfStreamException)
            {
                value = false;
                return false;
            }
            else
            {
                throw;
            }
        }
    }

    public static void TryReadBits(this BitReaderRtl reader, uint len, out int output)
    {
        try
        {
            output = reader.ReadBitsLeq32(len);
            return;
        }
        catch (IOException e)
        {
            // If the error is an end-of-stream error, return false without an error
            if (e.InnerException is EndOfStreamException)
            {
                output = 0;
            }
            else
            {
                throw;
            }
        }
    }
}