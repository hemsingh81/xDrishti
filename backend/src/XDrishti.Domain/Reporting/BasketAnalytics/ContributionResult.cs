namespace XDrishti.Domain.Reporting;

/// <summary>Who made the basket's return (BK-06 m15).</summary>
/// <param name="ReturnPct">The basket's buy-and-hold return in percent (P8-07).</param>
/// <param name="Top3Share">The share of the return from the three largest contributors; the state of the shares (not evaluable when the return is zero).</param>
/// <param name="Included">Members the return rests on.</param>
/// <param name="Rows">Every included member by share, largest first; empty unless <paramref name="Top3Share"/> is a value.</param>
/// <param name="Top">The five largest contributors.</param>
/// <param name="Bottom">The five smallest, smallest first.</param>
/// <param name="Excluded">Members left out of the hold, named (P8-07).</param>
public sealed record ContributionResult(
    Metric ReturnPct,
    Metric Top3Share,
    int Included,
    IReadOnlyList<ContributionRow> Rows,
    IReadOnlyList<ContributionRow> Top,
    IReadOnlyList<ContributionRow> Bottom,
    IReadOnlyList<BuyAndHoldExclusion> Excluded);
