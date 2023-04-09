using System.Numerics;

namespace Wavee.Audio.Codecs;

/// <summary>
/// A <see cref="TimeBase"/> is the conversion factor between time, expressed in seconds, and a `TimeStamp` or
/// `Duration`.
///
/// In other words, a `TimeBase` is the length in seconds of one tick of a `TimeStamp` or
/// `Duration`.
/// </summary>
/// <param name="Numer">The numerator.</param>
/// <param name="Denom">The denominator.</param>
public readonly record struct TimeBase(uint Numer, uint Denom)
{
    public ulong CalcTimestamp(TimeSpan time)
    {
        //the dividing factor
        var k = 1.0 / ((double)(Numer));

        // Multiplying seconds by the denominator requires
        // up-to 96-bits (32-bit timebase
        // denominator * 64-bit timestamp).
        var product = new BigInteger(time.TotalSeconds)
                      * new BigInteger(Denom);

        // Like calc_time, a 64-bit floating-point value only has 52-bits of integer precision.
        // If the product requires more than 52-bits, split the product into upper and lower parts
        // and multiply by k separately, before adding back together.

        ulong a = (ulong)((double)product * k);
        if (product > ((long)1 << 52))
        {
            //Split the 96-bit product into 48 bit upper and lower parts
            var u = (ulong)((product & ~0xffff_ffff_ffff >> 48));
            var l = (ulong)(((product & 0xffff_ffff_ffff)) >> 0);

            var uk = (ulong)((double)u * k);
            var ul = (ulong)((double)l * k);

            a = Math.Max((((ulong)uk) << 48) + ul, 0);
        }
        
        // The fractional portion can be calculate directly using floating point arithemtic.
        var b = (ulong)((double)(time.Milliseconds * Denom) * k);
        
        // Add the two parts together.
        return a + b;
    }
}