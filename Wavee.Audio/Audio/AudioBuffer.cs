using System.Buffers;
using Wavee.Audio.Codecs;

namespace Wavee.Audio.Audio;

public sealed class AudioBuffer<S> where S : unmanaged
{
    private Memory<S> buf;
    private SignalSpec spec;
    private int nFrames;
    private int nCapacity;

    public AudioBuffer(ulong duration, SignalSpec spec)
    {
        ulong nSamples = (ulong)duration * (ulong)spec.Channels.Count();
        unsafe
        {
            if (nSamples > (ulong)(int.MaxValue / sizeof(S)))
            {
                throw new ArgumentException("Duration too large");
            }
        }

        buf = new S[(int)nSamples];
        for (int i = 0; i < (int)nSamples; ++i)
        {
            buf.Span[i] = default;
        }

        this.spec = spec;
        nFrames = 0;
        nCapacity = (int)((int)nSamples / spec.Channels.Count());
    }

    private AudioBuffer()
    {
    }

    public static AudioBuffer<S> Unused()
    {
        return new AudioBuffer<S>
        {
            buf = Memory<S>.Empty,
            spec = new SignalSpec(0, default(Channels)),
            nFrames = 0,
            nCapacity = 0,
        };
    }

    public bool IsUnused()
    {
        return nCapacity == 0;
    }

    public SignalSpec Spec()
    {
        return spec;
    }

    public int Capacity()
    {
        return nCapacity;
    }

    public Span<S> ChanMut(int channels)
    {
        var start = channels * nCapacity;

        if (start + nCapacity > buf.Length)
        {
            throw new IndexOutOfRangeException();
        }

        return buf.Span.Slice(start, nFrames);
    }

    public void Clear()
    {
        nFrames = 0;
    }

    /// <summary>
    /// Trims samples from the start and end of the buffer.
    /// </summary>
    /// <param name="packetTrimStart"></param>
    /// <param name="packetTrimEnd"></param>
    /// <exception cref="NotImplementedException"></exception>
    public void Trim(uint start, uint end)
    {
        // First, trim the end to reduce the number of frames have to be shifted when the front is
        // trimmed.
        Truncate((uint)(Math.Min(uint.MaxValue, nFrames + end)));

        Shift(start);
    }

    private void Shift(uint shift)
    {
        if (shift >= nFrames)
            Clear();
        else if (shift > 0)
        {
            // Shift the samples down in each plane.
            int chunkSize = this.nCapacity;
            int totalChunks = (int)Math.Ceiling((double)buf.Length / chunkSize);

            for (int i = 0; i < totalChunks; i++)
            {
                int chunkStart = i * chunkSize;
                int chunkEnd = Math.Min(chunkStart + chunkSize, buf.Length);
                int copyStart = (int)(chunkStart + shift);
                int copyEnd = Math.Min(copyStart + this.nFrames, chunkEnd);

                if (copyStart < chunkEnd && copyEnd <= chunkEnd)
                {
                    buf.Slice(copyStart, copyEnd - copyStart).CopyTo(buf[chunkStart..]);
                }
            }

            nFrames -= (int)shift;
        }
    }

    private void Truncate(uint frames)
    {
        if (frames < nFrames)
        {
            nFrames = (int)frames;
        }
    }

    public void RenderReserve(int? renderLen)
    {
        var nReservedFrames = renderLen ?? (nCapacity - nFrames);
        if (nFrames + nReservedFrames > nCapacity)
        {
            throw new ArgumentException("Capacity will be exceeded");
        }

        nFrames += nReservedFrames;
    }


    public int Frames => nFrames;
    public ReadOnlySpan<S> Buffer => buf.Span;

    private S[] GetChannel(int ch)
    {
        var start = ch * nCapacity;

        if (start + nCapacity > buf.Length)
        {
            throw new IndexOutOfRangeException();
        }

        return buf.Slice(start, nFrames).ToArray();
    }

    public ReadOnlySpan<S> Chan(int ch)
    {
        var start = ch * nCapacity;

        if (start + nCapacity > buf.Length)
        {
            throw new IndexOutOfRangeException();
        }

        return buf.Span.Slice(start, nFrames);
    }
}