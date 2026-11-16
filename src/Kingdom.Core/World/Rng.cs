namespace Kingdom.Core.World;

/// <summary>
/// Small deterministic random number generator (SplitMix64).
/// Unlike System.Random, its output is guaranteed identical on every platform and .NET version,
/// which keeps worlds reproducible from their seed.
/// </summary>
public sealed class Rng
{
    private ulong _state;

    public Rng(ulong seed) => _state = seed;

    /// <summary>Derives an independent generator for a sub-system, so adding a feature doesn't shift other features.</summary>
    public static Rng For(ulong seed, ulong stream) => new(Mix(seed ^ (stream * 0x9E3779B97F4A7C15UL)));

    public ulong NextULong()
    {
        _state += 0x9E3779B97F4A7C15UL;
        return Mix(_state);
    }

    /// <summary>Uniform double in [0, 1).</summary>
    public double NextDouble() => (NextULong() >> 11) * (1.0 / (1UL << 53));

    /// <summary>Uniform int in [min, maxExclusive).</summary>
    public int NextInt(int min, int maxExclusive)
    {
        if (maxExclusive <= min) return min;
        return min + (int)(NextULong() % (ulong)(maxExclusive - min));
    }

    /// <summary>Uniform double in [min, max).</summary>
    public double Range(double min, double max) => min + (max - min) * NextDouble();

    private static ulong Mix(ulong z)
    {
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}
