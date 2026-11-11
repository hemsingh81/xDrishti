using XDrishti.Domain.Selection.SmartRules;

namespace XDrishti.Domain.Reporting;

/// <summary>
/// The context row of a basket for one day (spec BK-08, criteria 7 and 8): the BK-06 kernels (<see cref="BasketBreadth"/>, <see cref="Dispersion"/>, <see cref="PairwiseCorrelation"/>) called with the members of the day, so the tracked-set breadth and the basket breadth are one function.
/// Nothing after <c>asOf</c> is read: the kernels drop later closes, so appending bars after the day changes no row of that day or earlier. Pure.
/// </summary>
public static class BasketContextCalculator
{
    /// <summary>Computes the row. <paramref name="members"/> are the members as of the day with their closes (later closes are ignored).</summary>
    public static BasketContextRow Compute(Guid basketId, DateOnly asOf, IReadOnlyList<MemberSeries> members, string membershipBasis, BasketProfileOptions options)
    {
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(options);
        var breadth = BasketBreadth.Compute(members, asOf, options);
        var returns = members.ToDictionary(m => m.Symbol, m => ReturnSeries.FromCloses(m.Closes.Select(c => (c.Date, c.Close)), asOf), StringComparer.Ordinal);
        var dispersion = Dispersion.Compute(returns, asOf, options).Stdev;
        var correlation = PairwiseCorrelation.Compute(returns, new Dictionary<string, double>(), asOf, options).AveragePairwise;
        var enough = breadth.AboveSma20.Share.State == MetricState.Value;
        return new BasketContextRow(
            basketId,
            asOf,
            members.Count,
            breadth.AboveSma20.Voters,
            enough ? Number(breadth.AboveSma20.Share) : null,
            Number(breadth.AboveSma50.Share),
            Number(breadth.AboveSma200.Share),
            Number(breadth.AdvanceShare.Share),
            Number(breadth.NewHighShare.Share),
            Number(dispersion),
            Number(correlation),
            enough ? BasketContextRow.ValueState : BasketContextRow.InsufficientState,
            membershipBasis);
    }

    private static double? Number(Metric metric) => metric.State == MetricState.Value ? metric.Number : null;
}
