namespace XDrishti.SimOracle;

/// <summary>The SplitMix64 generator with its published constants (no System.Random).</summary>
internal sealed class SplitMix64
{
    private ulong _state;

    internal SplitMix64(ulong seed) => _state = seed;

    internal ulong Next()
    {
        _state = unchecked(_state + 0x9E3779B97F4A7C15UL);
        ulong z = _state;
        z = unchecked((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL);
        z = unchecked((z ^ (z >> 27)) * 0x94D049BB133111EBUL);
        return z ^ (z >> 31);
    }

    /// <summary>An integer in [low, high] inclusive.</summary>
    internal int Range(int low, int high) => low + (int)(Next() % (ulong)(high - low + 1));

    internal bool Chance(int percent) => Range(0, 99) < percent;
}
