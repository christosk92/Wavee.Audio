// See https://aka.ms/new-console-template for more information

using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using NAudio.Wave;
using Wavee.Audio;
using Wavee.Audio.Audio;
using Wavee.Audio.Codecs;
using Wavee.Audio.IO;
using Wavee.Audio.Vorbis;

while (true)
{
    var testFile = "C:\\Users\\ckara\\Downloads\\ifeelyou.ogg";
    var fs = File.Open(testFile, FileMode.Open, FileAccess.Read, FileShare.Read);
    var sw = Stopwatch.StartNew();
    var mss = new MediaSourceStream(fs, new MediaSourceStreamOptions(64 * 1024));
    var reader = new OggReader(mss, new FormatOptions());
    var track = reader.DefaultTrack;
    var decoder = new VorbisDecoder(track.CodecParameters, new DecoderOptions(false));
    sw.Stop();

    AudioOutput? audioOutput = default;
    while (true)
    {
        var packet = reader.NextPacket();
        var decoded = decoder.Decode(packet);
        if (audioOutput is null)
        {
            var spec = decoded.Spec();

            var duration = decoded.Capacity();

            audioOutput = AudioOutput.TryOpen(spec, (ulong)duration);
        }


        // Write the decoded audio samples to the audio output if the presentation timestamp
        // for the packet is >= the seeked position (0 if not seeking).
        if (audioOutput is not null)
        {
            audioOutput.Write(decoded);
        }
    }

    reader.Dispose();
    decoder.Dispose();
    GC.Collect();
}

var mn = new ManualResetEvent(false);
mn.WaitOne();

sealed class AudioOutput
{
    private readonly WaveOutEvent _wavePlayer;
    private readonly BufferedWaveProvider _bufferedWaveProvider;
    private readonly WaveFormat _waveFormat;
    private SampleBuffer<float> _sampleBuffer = default;

    private AudioOutput(WaveOutEvent wavePlayer,
        BufferedWaveProvider bufferedWaveProvider,
        WaveFormat waveFormat,
        SampleBuffer<float> sampleBuffer)
    {
        _wavePlayer = wavePlayer;
        _bufferedWaveProvider = bufferedWaveProvider;
        _waveFormat = waveFormat;
        _sampleBuffer = sampleBuffer;
        //            let ring_len = ((200 * config.sample_rate.0 as usize) / 1000) * num_channels;
    }

    public static AudioOutput? TryOpen(SignalSpec spec, ulong duration)
    {
        var wavePlayer = new WaveOutEvent();
        var waveFormat = WaveFormat.CreateIeeeFloatWaveFormat((int)spec.Rate, (int)spec.Channels.Count());
        var bufferedWaveProvider = new BufferedWaveProvider(waveFormat);
        wavePlayer.Init(bufferedWaveProvider);
        wavePlayer.Play();
        return new AudioOutput(wavePlayer, bufferedWaveProvider, waveFormat,
            new SampleBuffer<float>((ulong)duration, spec));
    }

    public void Write(AudioBuffer<float> decoded)
    {
        if (decoded.Frames == 0)
            return;

        _sampleBuffer.CopyInterleavedRef(decoded);
        var samples = _sampleBuffer.Samples();
        //convert float to byte
        //var bytes = MemoryMarshal.Cast<float, byte>(samples).ToArray();
        var bytes = new byte[samples.Length * sizeof(float)];
        Buffer.BlockCopy(samples.ToArray(), 0, bytes, 0, bytes.Length);
        _bufferedWaveProvider.AddSamples(bytes, 0,
            bytes.Length);
        while (_bufferedWaveProvider.BufferedDuration.TotalSeconds > 0.5)
        {
            Thread.Sleep(5);
        }
    }
}


/// <summary>
/// A `SampleBuffer`, is a sample oriented buffer. It is agnostic to the ordering/layout of samples
/// within the buffer. `SampleBuffer` is mean't for safely importing and exporting sample data to
/// and from Symphonia using the sample's in-memory data-type.
/// </summary>
public class SampleBuffer<S> where S : unmanaged
{
    private Memory<S> _buf;
    private int _nWritten;

    public SampleBuffer(ulong duration, SignalSpec spec)
    {
        // The number of channels * duration cannot exceed u64::MAX.
        if (duration > ulong.MaxValue / spec.Channels.Count())
            throw new ArgumentOutOfRangeException(nameof(duration));

        // The total number of samples the buffer will store.
        var nSamples = duration * spec.Channels.Count();

        // Practically speaking, it is not possible to allocate more than usize::MAX bytes of
        // samples. This assertion ensures the potential downcast of n_samples to usize below is
        // safe.
        if (nSamples > (ulong)(int.MaxValue / Marshal.SizeOf<S>()))
            throw new ArgumentOutOfRangeException(nameof(duration));

        // Allocate enough memory for all the samples and fill the buffer with silence.
        _buf = new S[(int)nSamples];
        _buf.Span.Fill(default);
        _nWritten = 0;
    }

    public void CopyInterleavedRef(AudioBuffer<float> decoded)
    {
        var nChannels = decoded.Spec().Channels.Count();
        var nSamples = (int)(decoded.Frames * nChannels);

        // Ensure that the capacity of the sample buffer is greater than or equal to the number
        // of samples that will be copied from the source buffer.
        if (this.Capacity() < nSamples)
            throw new ArgumentOutOfRangeException(nameof(decoded));

        // Interleave the source buffer channels into the sample buffer.
        for (int ch = 0; ch < nChannels; ch++)
        {
            var chSlice = decoded.Chan(ch);

            for (int i = 0; i < chSlice.Length; i++)
            {
                var n = (int)(ch + i * nChannels);
                var val = chSlice[i];
                _buf.Span[n] = Unsafe.As<float, S>(ref val);
            }
        }

        _nWritten = nSamples;
    }

    private long Capacity() => _buf.Length;

    public ReadOnlySpan<S> Samples() => _buf.Span[.._nWritten];
}