using XDrishti.Domain.Research;

namespace XDrishti.SimExport.Cards;

/// <summary>
/// The one seeded-order primitive (spec P3-04c, C1): a sample with no random source. The rank of a key is the lower-case hex SHA-256 of <c>&lt;seed&gt;|&lt;key&gt;</c>; keys are ordered by rank, ascending, ties (which cannot happen for
/// distinct keys of a real run) by the key. The order does not depend on the order the keys are given in, is the same for the same seed and changes when the seed changes. The card selection uses the run key as the seed; the stratified
/// sampler of P3-09 (strategy × instrument × year) uses the same function.
/// </summary>
public static class SeededRank
{
    /// <summary>The rank of one key.</summary>
    public static string Of(string seed, string key)
    {
        ArgumentNullException.ThrowIfNull(seed);
        ArgumentNullException.ThrowIfNull(key);
        return CanonicalText.Sha256Hex(seed + "|" + key);
    }

    /// <summary>The keys ordered by rank, ascending.</summary>
    public static IReadOnlyList<string> Order(string seed, IEnumerable<string> keys)
    {
        ArgumentNullException.ThrowIfNull(seed);
        ArgumentNullException.ThrowIfNull(keys);
        return [.. keys.Select(k => (Key: k, Rank: Of(seed, k))).OrderBy(p => p.Rank, StringComparer.Ordinal).ThenBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key)];
    }
}
