using Wavee.Audio.Codecs;

namespace Wavee.Audio.Vorbis.Mapping.Mappers;

internal static class VorbisChannels
{
    public static bool TryGetChannels(byte numChannels, out Channels? channels)
    {
        switch (numChannels)
        {
            case 1:
                channels = Channels.FRONT_LEFT;
                break;
            case 2:
                channels = Channels.FRONT_LEFT | Channels.FRONT_RIGHT;
                break;
            case 3:
                channels = Channels.FRONT_LEFT | Channels.FRONT_CENTER | Channels.FRONT_RIGHT;
                break;
            case 4:
                channels = Channels.FRONT_LEFT | Channels.FRONT_RIGHT | Channels.REAR_LEFT | Channels.REAR_RIGHT;
                break;
            case 5:
                channels = Channels.FRONT_LEFT | Channels.FRONT_CENTER | Channels.FRONT_RIGHT | Channels.REAR_LEFT |
                           Channels.REAR_RIGHT;
                break;
            case 6:
                channels = Channels.FRONT_LEFT | Channels.FRONT_CENTER | Channels.FRONT_RIGHT | Channels.REAR_LEFT |
                           Channels.REAR_RIGHT | Channels.LFE1;
                break;
            case 7:
                channels = Channels.FRONT_LEFT | Channels.FRONT_CENTER | Channels.FRONT_RIGHT | Channels.SIDE_LEFT |
                           Channels.SIDE_RIGHT | Channels.REAR_CENTER | Channels.LFE1;
                break;
            case 8:
                channels = Channels.FRONT_LEFT | Channels.FRONT_CENTER | Channels.FRONT_RIGHT | Channels.SIDE_LEFT |
                           Channels.SIDE_RIGHT | Channels.REAR_LEFT | Channels.REAR_RIGHT | Channels.LFE1;
                break;
            default:
                channels = null;
                return false;
        }

        return true;
    }
}