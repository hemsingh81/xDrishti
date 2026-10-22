using System.Globalization;

namespace XDrishti.Domain.Lab;

/// <summary>A small deterministic generator (SplitMix64) so a seed gives the same draws on every machine and runtime version; used by the robustness checks (spec P5-05).</summary>
public sealed class SeededRandom(ulong seed)
{
    private ulong _state = seed;

    /// <summary>The next 64 random bits.</summary>
    public ulong NextBits()
    {
        _state += 0x9E3779B97F4A7C15UL;
        var z = _state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>A number in <c>[0, bound)</c>; <paramref name="bound"/> must be above 0.</summary>
    public int Next(int bound)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(bound, 0);
        return (int)(((NextBits() >> 32) * (ulong)bound) >> 32);
    }

    /// <summary>The seed of a run: the first 8 bytes of the sha256 of its key.</summary>
    public static ulong SeedOf(string runKey)
    {
        ArgumentNullException.ThrowIfNull(runKey);
        return ulong.Parse(LabRunIdentity.Sha256(runKey)[..16], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }
}
