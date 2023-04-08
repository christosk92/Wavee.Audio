namespace Wavee.Audio.Vorbis.Mapping;

public interface IMapResult
{
    public record StreamData(ulong Dur) : IMapResult;

    public record SideData(ISideData Data) : IMapResult;

    public record SetupData : IMapResult;
    public record ErrorData(System.Exception Error) : IMapResult;
}