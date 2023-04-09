using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Wavee.Audio.Audio;
using Wavee.Audio.Codecs;

namespace Wavee.Audio.Vorbis.Convenience;

/// <summary>
/// A `SampleBuffer`, is a sample oriented buffer. It is agnostic to the ordering/layout of samples
/// within the buffer. `SampleBuffer` is mean't for safely importing and exporting sample data to
/// and from Symphonia using the sample's in-memory data-type.
/// </summary>
public sealed class SampleBuffer<S> where S : unmanaged
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

        _zeroes = new S[(int)nSamples];
        _zeroes.Span.Fill(default);
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

    public ReadOnlySpan<S> Samples() =>
        _nWritten != 0 ? _buf.Span[.._nWritten] : _zeroes.Span;

    private static Memory<S> _zeroes;
}