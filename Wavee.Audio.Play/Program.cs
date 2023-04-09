// See https://aka.ms/new-console-template for more information

using System.Diagnostics;
using NAudio.Wave;
using Wavee.Audio.Vorbis.Convenience;

while (true)
{
    var testFile = "C:\\Users\\ckara\\Downloads\\ifeelyou.ogg";
    var fs = File.Open(testFile, FileMode.Open, FileAccess.Read, FileShare.Read);
    var sw = Stopwatch.StartNew();
    var reader = new VorbisReader(fs, true);
    var output = new AudioOutput(reader);
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
    }

    reader.Dispose();
    output.Dispose();
    GC.Collect();
}

var mn = new ManualResetEvent(false);
mn.WaitOne();

sealed  class AudioOutput : IDisposable
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
}