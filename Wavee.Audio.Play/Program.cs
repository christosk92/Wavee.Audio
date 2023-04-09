// See https://aka.ms/new-console-template for more information

using System.Diagnostics;
using NAudio.Wave;
using Wavee.Audio.Vorbis.Convenience;



ManualResetEvent _waitForPlayback = new ManualResetEvent(false);
AudioOutput? output = default;
Task.Run(() =>
{
    var testFile = "C:\\Users\\ckara\\Downloads\\ifeelyou.ogg";
    var fs = File.Open(testFile, FileMode.Open, FileAccess.Read, FileShare.Read);
    var sw = Stopwatch.StartNew();
    var reader = new VorbisReader(fs,
        true,
        true);
    output = new AudioOutput(reader);
    sw.Stop();

    while (true)
    {
        var packet = reader.ReadSamples();
        // Write the decoded audio samples to the audio output if the presentation timestamp
        // for the packet is >= the seeked position (0 if not seeking)
        if (packet.Length == 0)
        {
            break;
        }

        output.Write(packet);
        _waitForPlayback.Set();
    }

    reader.Dispose();
    output.Dispose();
    GC.Collect();
});

//Commands:
//--pause
//--play
//--seek 00:00:00.000
while (true)
{
    var input = Console.ReadLine();
    if (input == "--pause")
    {
        output.Pause();
    }
    else if (input == "--play")
    {
        output.Play();
    }
    else if (input.StartsWith("--seek"))
    {
        var time = input.Split(" ")[1];
        var timeSpan = TimeSpan.Parse(time);
        output.Seek(timeSpan);
    }
}


sealed class AudioOutput : IDisposable
{
    private readonly WaveOutEvent _wavePlayer;
    private readonly BufferedWaveProvider _bufferedWaveProvider;
    private readonly WaveFormat _waveFormat;
    private readonly VorbisReader _reader;

    public AudioOutput(VorbisReader reader)
    {
        _wavePlayer = new WaveOutEvent();
        _waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(reader.SampleRate,
            reader.Channels);
        _bufferedWaveProvider = new BufferedWaveProvider(_waveFormat);
        _wavePlayer.Init(_bufferedWaveProvider);
        _wavePlayer.Play();
        _reader = reader;
    }

    public void Write(ReadOnlySpan<byte> samples)
    {
        if (samples.Length == 0)
            return;
        var samplesArr = samples.ToArray();
        _bufferedWaveProvider.AddSamples(samplesArr, 0,
            samples.Length);
        while (_bufferedWaveProvider.BufferedDuration.TotalSeconds > 0.5)
        {
            Thread.Sleep(5);
        }
    }

    public void Dispose()
    {
        _wavePlayer.Dispose();
        _reader.Dispose();
    }

    public void Pause()
    {
        _wavePlayer.Pause();
    }
    public void Play()
    {
        _wavePlayer.Play();
    }

    public void Seek(TimeSpan to)
    {
        _reader
            .Seek(to);
    }
}