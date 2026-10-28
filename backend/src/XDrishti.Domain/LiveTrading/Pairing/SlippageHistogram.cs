namespace XDrishti.Domain.LiveTrading;

/// <summary>One bin of the histogram in basis points: <see cref="Lo"/> inclusive, <see cref="Hi"/> exclusive; null is open.</summary>
public sealed record HistogramBin(double? Lo, double? Hi, int Count);

/// <summary>The fixed-bin histogram of live total entry slippage in basis points (spec P11-03 criterion 7): edges -10, -5, -2, 0, 2, 5, 10, 20, lower side inclusive, every bin drawn (a zero count is a zero), fewer than 10 observations gives no bins.</summary>
public static class SlippageHistogram
{
    public const int MinimumObservations = 10;

    private static readonly double[] Edges = [-10, -5, -2, 0, 2, 5, 10, 20];

    public static IReadOnlyList<HistogramBin>? Build(IReadOnlyList<double> basisPoints)
    {
        ArgumentNullException.ThrowIfNull(basisPoints);
        var values = basisPoints.Where(double.IsFinite).ToList();
        if (values.Count < MinimumObservations)
        {
            return null;
        }

        var counts = new int[Edges.Length + 1];
        foreach (var v in values)
        {
            var i = 0;
            while (i < Edges.Length && v >= Edges[i])
            {
                i++;
            }

            counts[i]++;
        }

        return [.. counts.Select((c, i) => new HistogramBin(i == 0 ? null : Edges[i - 1], i == Edges.Length ? null : Edges[i], c))];
    }
}
