namespace XDrishti.Domain.Learning;

/// <summary>
/// The one-extra-dimension rule of BK-11 (ADR 0261): the key has exactly one universe part with the values <c>*</c> and three tiers, so the cells are at most four times the plain ones. Checked at startup and by tests; a second universe-like
/// dimension changes <see cref="CellId.Tiers"/> or the key and fails here.
/// </summary>
public static class CellExplosionGuard
{
    /// <summary>The most cells a plain cell becomes: itself and one per tier.</summary>
    public const int MaxFactor = 4;

    /// <summary>True when the tier list is exactly three and the longest key has the one extra part (eight parts: six, universe, expression).</summary>
    public static bool Holds()
    {
        var wide = new CellId("s", "t", "BUY", "intraday", "e", "*", CellId.Tiers[0], Expression.Pair).Key.Split('|').Length;
        return CellId.Tiers.Count == 3 && wide == 8;
    }

    /// <summary>True when <paramref name="cells"/> number at most <see cref="MaxFactor"/> times the plain (universe <c>*</c>) cells among them.</summary>
    public static bool WithinBound(IEnumerable<CellId> cells)
    {
        ArgumentNullException.ThrowIfNull(cells);
        var all = cells.ToList();
        return all.Count <= MaxFactor * all.Count(c => c.Universe == CellId.AllUniverses);
    }
}
