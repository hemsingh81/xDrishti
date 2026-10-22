using System.Security.Cryptography;
using System.Text;

namespace XDrishti.Domain.Lab;

/// <summary>
/// The instruments of a quick test (spec P5-02, D2): all of them up to the limit, otherwise the first <c>limit</c> of a seeded shuffle of the sorted ids. The seed is the first eight bytes of the SHA-256 of the sorted ids, so the same universe gives
/// the same sample on any machine and any release; the generator is written out here (SplitMix64) because the platform's random generator is not promised to be stable.
/// </summary>
public static class QuickSample
{
    /// <summary>What was chosen: the ids in ascending order, whether the universe was cut, and the seed that was used (null when nothing was cut).</summary>
    public sealed record Choice(IReadOnlyList<Guid> Ids, bool Sampled, long? Seed);

    /// <summary>Chooses the instruments. The ids are de-duplicated and sorted first.</summary>
    public static Choice Choose(IEnumerable<Guid> ids, int limit)
    {
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        var sorted = ids.Distinct().Order().ToList();
        if (sorted.Count <= limit)
        {
            return new Choice(sorted, false, null);
        }

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(",", sorted.Select(i => i.ToString("D")))));
        var seed = BitConverter.ToInt64(digest, 0);
        var state = unchecked((ulong)seed);
        var shuffled = sorted.ToArray();
        for (var i = shuffled.Length - 1; i > 0; i--)
        {
            var j = (int)(Next(ref state) % (ulong)(i + 1));
            (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
        }

        return new Choice([.. shuffled.Take(limit).Order()], true, seed);
    }

    private static ulong Next(ref ulong state)
    {
        state = unchecked(state + 0x9E3779B97F4A7C15UL);
        var z = state;
        z = unchecked((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL);
        z = unchecked((z ^ (z >> 27)) * 0x94D049BB133111EBUL);
        return z ^ (z >> 31);
    }
}
