using System.Globalization;
using XDrishti.Domain.Market.Context;

namespace XDrishti.Domain.Learning;

/// <summary>One regime part with the trades that fell in it.</summary>
/// <param name="Part">The cell part (<see cref="RegimeLabel.CellPart"/>).</param>
/// <param name="Name">The regime name.</param>
/// <param name="Basis"><see cref="ContextBasis"/>.</param>
/// <param name="Trades">How many trades.</param>
/// <param name="MeanR">Their mean R.</param>
/// <param name="TotalR">Their total R.</param>
/// <param name="Enough">At least <see cref="RegimeBreakdown.MinTrades"/> trades; a bucket below it is shown but is not evidence.</param>
public sealed record RegimeBucket(string Part, string Name, string Basis, int Trades, double MeanR, double TotalR, bool Enough);

/// <summary>The trades split by regime, the tally of the labels and a one-line text.</summary>
public sealed record RegimeBreakdownResult(IReadOnlyList<RegimeBucket> Buckets, RegimeTally Tally, string Text);

/// <summary>
/// The out-of-sample trades of a Lab run split by the regime of their entry day (P6-02, ADR 0146; the <c>by_regime</c> block of ADR 0062). Pure. The two bases are separate buckets; trades with no label are counted by reason, never put in a bucket.
/// </summary>
public static class RegimeBreakdown
{
    /// <summary>The trades a bucket needs to count as evidence (the same 30 as the cell statistics).</summary>
    public const int MinTrades = 30;

    public static RegimeBreakdownResult Of(IReadOnlyList<(RegimeLabel Label, double R)> trades)
    {
        ArgumentNullException.ThrowIfNull(trades);
        var tally = RegimeTally.Of(trades.Select(t => t.Label));
        var buckets = trades.Where(t => t.Label.IsAvailable)
            .GroupBy(t => t.Label.CellPart, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g =>
            {
                var first = g.First().Label;
                var sum = g.Sum(t => t.R);
                return new RegimeBucket(g.Key, first.Name!, first.Basis!, g.Count(), sum / g.Count(), sum, g.Count() >= MinTrades);
            })
            .ToList();
        return new RegimeBreakdownResult(buckets.AsReadOnly(), tally, TextOf(buckets, tally, trades.Count));
    }

    private static string TextOf(List<RegimeBucket> buckets, RegimeTally tally, int count)
    {
        if (count == 0)
        {
            return "unavailable: there are no out-of-sample trades";
        }

        var gone = tally.Unavailable == 0 ? string.Empty : "unavailable " + string.Join(", ", tally.UnavailableByReason.Select(r => r.Key + " " + r.Value.ToString(CultureInfo.InvariantCulture)));
        if (buckets.Count == 0)
        {
            return "unavailable: no out-of-sample trade has a regime (" + gone + ")";
        }

        var parts = buckets.Select(b => string.Create(
            CultureInfo.InvariantCulture,
            $"{b.Part} {b.Trades} trades, mean {b.MeanR:0.00} R{(b.Enough ? string.Empty : " (not enough evidence)")}"));
        return string.Join("; ", parts) + (gone.Length == 0 ? string.Empty : "; " + gone);
    }
}
