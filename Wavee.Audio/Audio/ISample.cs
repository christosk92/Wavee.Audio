using System.Runtime.CompilerServices;
using Wavee.Audio.Codecs;

namespace Wavee.Audio.Audio;

public static class SampleExtensions
{
    public static SampleFormat GetFormat(this object val)
    {
        return val switch
        {
            float _ => SampleFormat.F32,
            _ => throw new NotImplementedException()
        };
    }

    public static uint GetEffectiveBits(this SampleFormat format)
    {
        return format switch
        {
            SampleFormat.F32 => 24,
            _ => throw new NotImplementedException()
        };
    }

    public static T GetMiddle<T>(this SampleFormat format) where T : unmanaged
    {
        return format switch
        {
            SampleFormat.F32 => Unsafe.As<float, T>(ref Unsafe.AsRef(0.0f)),
            _ => throw new NotImplementedException()
        };
    }

    public static T Clamp<T>(this SampleFormat format, T value) where T : unmanaged
    {
        return format switch
        {
            SampleFormat.F32 => Unsafe.As<float, T>(ref Unsafe.AsRef(ClampF32(Unsafe.As<T, float>(ref value)))),
            _ => throw new NotImplementedException()
        };
    }

    private static float ClampF32(float val)
    {
        // This slightly inelegant code simply returns min(max(1.0, val), -1.0). In release mode on
        // platforms with SSE2 support, it will compile down to 4 SSE instructions with no branches,
        // thereby making it the most performant clamping implementation for floating-point samples.
        var clamped = val;
        clamped = clamped > 1.0f ? 1.0f : clamped;
        clamped = clamped < -1.0f ? -1.0f : clamped;
        return clamped;
    }
}