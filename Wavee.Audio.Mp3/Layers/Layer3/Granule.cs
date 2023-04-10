namespace Wavee.Audio.Mp3.Layers.Layer3;

internal class Granule
{
    public Granule()
    {
        Channels = new GranuleChannel[2];
        for (int i = 0; i < 2; i++)
        {
            Channels.Span[i] = new GranuleChannel();
        }
    }

    /// <summary>
    /// Channels in the granule.
    /// </summary>
    public Memory<GranuleChannel> Channels { get; }
}