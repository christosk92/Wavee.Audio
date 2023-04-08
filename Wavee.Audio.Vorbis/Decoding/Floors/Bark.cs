namespace Wavee.Audio.Vorbis.Decoding.Floors;

internal static class Bark
{
    public static int[] Map(int n, ushort floorrate, ushort size)
    {
        var map = new int[n];

        var foobarMin = size - 1;
        var rate = (double)(floorrate);
        var rateby2n = rate / (2 * n);

        var c = (double)(size) / bark(0.5 * rate);

        //0 to n-1
        for (int i = 0; i < n; ++i)
        {
            var foobar = (int)Math.Floor(bark(rateby2n * (double)i) * c);
            map[i] = Math.Min(foobar, foobarMin);
        }

        return map;
    }

    private static double bark(double rate)
    {
        return 13.1 * Math.Atan(0.00074 * rate) + 2.24 * Math.Atan((rate * rate) / 1.85e6) + 1e-4 * rate;
    }
}