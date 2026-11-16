using System;

namespace Kingdom.Core.World;

/// <summary>
/// Seeded 2D gradient (Perlin) noise with fractal sums.
/// Output of <see cref="Sample"/> is roughly in [-1, 1].
/// </summary>
public sealed class Noise
{
    private readonly int[] _perm = new int[512];

    public Noise(Rng rng)
    {
        var p = new int[256];
        for (int i = 0; i < 256; i++) p[i] = i;
        for (int i = 255; i > 0; i--)
        {
            int j = rng.NextInt(0, i + 1);
            (p[i], p[j]) = (p[j], p[i]);
        }
        for (int i = 0; i < 512; i++) _perm[i] = p[i & 255];
    }

    public double Sample(double x, double y)
    {
        int xi = (int)Math.Floor(x);
        int yi = (int)Math.Floor(y);
        double xf = x - xi;
        double yf = y - yi;
        xi &= 255;
        yi &= 255;

        double u = Fade(xf);
        double v = Fade(yf);

        int aa = _perm[_perm[xi] + yi];
        int ab = _perm[_perm[xi] + yi + 1];
        int ba = _perm[_perm[xi + 1] + yi];
        int bb = _perm[_perm[xi + 1] + yi + 1];

        double x1 = Lerp(Grad(aa, xf, yf), Grad(ba, xf - 1, yf), u);
        double x2 = Lerp(Grad(ab, xf, yf - 1), Grad(bb, xf - 1, yf - 1), u);
        // Perlin 2D peaks around +-0.7; scale to roughly +-1.
        return Lerp(x1, x2, v) * 1.414;
    }

    /// <summary>Fractal Brownian motion: several octaves summed, normalized to roughly [-1, 1].</summary>
    public double Fbm(double x, double y, int octaves, double lacunarity = 2.0, double gain = 0.5)
    {
        double sum = 0, amp = 1, freq = 1, norm = 0;
        for (int i = 0; i < octaves; i++)
        {
            // Offset each octave so their lattices don't line up.
            sum += amp * Sample(x * freq + i * 17.31, y * freq - i * 9.73);
            norm += amp;
            amp *= gain;
            freq *= lacunarity;
        }
        return sum / norm;
    }

    private static double Fade(double t) => t * t * t * (t * (t * 6 - 15) + 10);
    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    private static double Grad(int hash, double x, double y)
    {
        switch (hash & 7)
        {
            case 0: return x + y;
            case 1: return -x + y;
            case 2: return x - y;
            case 3: return -x - y;
            case 4: return x;
            case 5: return -x;
            case 6: return y;
            default: return -y;
        }
    }
}
