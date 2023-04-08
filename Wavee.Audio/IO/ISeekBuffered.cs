namespace Wavee.Audio.IO;

public interface ISeekBuffered
{
    /// <summary>
    /// Seek within the buffered data to an absolute position in the stream. Returns the position
    /// seeked to.
    /// </summary>
    /// <param name="pos"></param>
    /// <returns></returns>
    ulong SeekBuffered(ulong pos);
    
    void EnsureSeekBuffered(int len);
}