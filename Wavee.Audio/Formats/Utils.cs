namespace Wavee.Audio.Formats;

public static class Utils
{
    public static void TrimPacket(Packet packet, uint delay, ulong? numFrames)
    {
        packet.TrimStart = (packet.Ts < delay) ? (uint)Math.Min(delay - packet.Ts, packet.Dur) : 0;

        if (packet.TrimStart > 0)
        {
            packet.Ts = 0;
            packet.Dur -= packet.TrimStart;
        }
        else
        {
            packet.Ts -= delay;
        }

        if (numFrames.HasValue)
        {
            packet.TrimEnd = (packet.Ts + packet.Dur > numFrames.Value)
                ? (uint)Math.Min(packet.Ts + packet.Dur - numFrames.Value, packet.Dur)
                : 0;

            if (packet.TrimEnd > 0)
            {
                packet.Dur -= packet.TrimEnd;
            }
        }
    }
}