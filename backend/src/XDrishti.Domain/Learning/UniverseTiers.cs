namespace XDrishti.Domain.Learning;

/// <summary>
/// The pure tier function of BK-11 (ADR 0261). On one date, the shares with at least <see cref="UniverseTierOptions.MinBars"/> bars and a median are the eligible ones, ranked by median traded value (highest first, ties by instrument id); the first
/// ceil(N/3) are <see cref="UniverseTier.High"/>, up to ceil(2N/3) <see cref="UniverseTier.Mid"/>, the rest <see cref="UniverseTier.Low"/>. A share that is not eligible is <see cref="UniverseTier.Unknown"/>; with N below the minimum every eligible share is
/// <see cref="UniverseTier.Unavailable"/>. The caller passes figures from bars before the decision date, so no later bar can change a tier.
/// </summary>
public static class UniverseTiers
{
    /// <summary>The tier of every candidate on the date.</summary>
    public static IReadOnlyDictionary<Guid, UniverseTier> Table(IReadOnlyList<TierCandidate> candidates, UniverseTierOptions options)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(options);
        var table = new Dictionary<Guid, UniverseTier>(candidates.Count);
        var eligible = new List<TierCandidate>();
        foreach (var c in candidates)
        {
            if (c.Bars >= options.MinBars && c.MedianTradedValue is >= 0)
            {
                eligible.Add(c);
            }
            else
            {
                table[c.InstrumentId] = UniverseTier.Unknown;
            }
        }

        var n = eligible.Count;
        if (n < options.MinEligible)
        {
            foreach (var c in eligible)
            {
                table[c.InstrumentId] = UniverseTier.Unavailable;
            }

            return table;
        }

        var highEnd = (n + 2) / 3;           // ceil(N/3)
        var midEnd = ((2 * n) + 2) / 3;      // ceil(2N/3)
        var ranked = eligible.OrderByDescending(c => c.MedianTradedValue).ThenBy(c => c.InstrumentId).ToList();
        for (var i = 0; i < n; i++)
        {
            table[ranked[i].InstrumentId] = i < highEnd ? UniverseTier.High : i < midEnd ? UniverseTier.Mid : UniverseTier.Low;
        }

        return table;
    }

    /// <summary>The tier of one share on the date; <see cref="UniverseTier.Unknown"/> when it is not among the candidates.</summary>
    public static UniverseTier Assign(IReadOnlyList<TierCandidate> candidates, Guid instrumentId, UniverseTierOptions options) =>
        Table(candidates, options).GetValueOrDefault(instrumentId, UniverseTier.Unknown);
}
