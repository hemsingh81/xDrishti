namespace XDrishti.Domain.Reporting;

/// <summary>
/// The cheapest guard against reading one lucky half (BK-07 m23, Decision 4): the closed trades of one strategy in a basket are split at the median exit date and the sign of the expectancy of each half is compared. Each half needs
/// <see cref="MinPerHalf"/> trades, else the cell is insufficient (with the n of each half). It is a median split of stored trades; it is not the locked hold-out of ADR 0207 and never reads that block. Pure.
/// </summary>
public static class HoldOutStability
{
    /// <summary>Fewest trades in each half.</summary>
    public const int MinPerHalf = 30;

    /// <summary>The two halves have the same sign of expectancy.</summary>
    public const string Agrees = "agrees";

    /// <summary>The signs differ.</summary>
    public const string Disagrees = "disagrees";

    /// <summary>A half has fewer than <see cref="MinPerHalf"/> trades.</summary>
    public const string Insufficient = "insufficient";

    /// <summary>A half has an expectancy of exactly zero, which has no sign.</summary>
    public const string NotEvaluable = "not_evaluable";

    /// <summary>One cell per strategy, in strategy order.</summary>
    public static IReadOnlyList<HoldOutCell> Compute(IEnumerable<HoldOutTrade> trades, int minPerHalf = MinPerHalf)
    {
        ArgumentNullException.ThrowIfNull(trades);
        return [.. trades.GroupBy(t => t.Strategy, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => Cell(g.Key, [.. g.Where(t => double.IsFinite(t.R)).OrderBy(t => t.ExitDate)], minPerHalf))];
    }

    private static HoldOutCell Cell(string strategy, List<HoldOutTrade> ordered, int min)
    {
        if (ordered.Count == 0)
        {
            var none = Metric.Empty("no trade");
            return new HoldOutCell(strategy, null, 0, 0, none, none, Insufficient);
        }

        var split = ordered[ordered.Count / 2].ExitDate;
        var first = ordered.Where(t => t.ExitDate < split).Select(t => t.R).ToArray();
        var second = ordered.Where(t => t.ExitDate >= split).Select(t => t.R).ToArray();
        var a = Half(first, min);
        var b = Half(second, min);
        if (a.State != MetricState.Value || b.State != MetricState.Value)
        {
            return new HoldOutCell(strategy, split, first.Length, second.Length, a, b, Insufficient);
        }

        var verdict = a.Number == 0 || b.Number == 0 ? NotEvaluable : Math.Sign(a.Number!.Value) == Math.Sign(b.Number!.Value) ? Agrees : Disagrees;
        return new HoldOutCell(strategy, split, first.Length, second.Length, a, b, verdict);
    }

    private static Metric Half(double[] r, int min) => r.Length < min ? Metric.Insufficient($"n = {r.Length}, at least {min} needed") : Metric.Of(r.Average());
}
