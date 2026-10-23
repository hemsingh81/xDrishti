namespace XDrishti.Domain.Selection;

/// <summary>
/// Pairwise return correlations between instruments, known before the decision date, plus optional "same underlying" groups (spec P7-06).
/// A pair with no figure is unknown, never zero. Symmetric; ordinal symbol comparison.
/// </summary>
public sealed class CorrelationTable
{
    private readonly Dictionary<(string, string), double> _pairs = [];
    private readonly Dictionary<string, string> _underlying;

    /// <summary>Creates a table.</summary>
    /// <param name="pairs">Correlations by symbol pair, in either order; non-finite values are ignored (unknown).</param>
    /// <param name="underlying">Symbol to underlying group; two symbols in the same group are the same bet without a figure.</param>
    public CorrelationTable(IEnumerable<(string A, string B, double Correlation)> pairs, IReadOnlyDictionary<string, string>? underlying = null)
    {
        ArgumentNullException.ThrowIfNull(pairs);
        foreach (var (a, b, c) in pairs)
        {
            if (double.IsFinite(c))
            {
                _pairs[Key(a, b)] = Math.Clamp(c, -1, 1);
            }
        }

        _underlying = underlying is null ? [] : new Dictionary<string, string>(underlying, StringComparer.Ordinal);
    }

    /// <summary>The correlation of two symbols, or null when unknown.</summary>
    public double? Get(string a, string b) => _pairs.TryGetValue(Key(a, b), out var c) ? c : null;

    /// <summary>True when both symbols are in the same underlying group.</summary>
    public bool SameUnderlying(string a, string b) =>
        _underlying.TryGetValue(a, out var ga) && _underlying.TryGetValue(b, out var gb) && string.Equals(ga, gb, StringComparison.Ordinal);

    /// <summary>
    /// Builds the table from daily returns: Pearson correlation of the last <paramref name="lookbackDays"/> common sessions strictly before <paramref name="decisionDate"/>; a pair with fewer than <paramref name="minCommon"/> common sessions is unknown.
    /// </summary>
    public static CorrelationTable FromReturns(IReadOnlyDictionary<string, IReadOnlyList<DailyReturn>> returns, DateOnly decisionDate, int lookbackDays = 60, int minCommon = 30, IReadOnlyDictionary<string, string>? underlying = null)
    {
        ArgumentNullException.ThrowIfNull(returns);
        var symbols = returns.Keys.Order(StringComparer.Ordinal).ToList();
        var byDate = symbols.ToDictionary(
            s => s,
            s => returns[s].Where(r => r.Date < decisionDate && double.IsFinite(r.Value)).GroupBy(r => r.Date).ToDictionary(g => g.Key, g => g.First().Value),
            StringComparer.Ordinal);
        var pairs = new List<(string, string, double)>();
        for (var i = 0; i < symbols.Count; i++)
        {
            for (var j = i + 1; j < symbols.Count; j++)
            {
                var a = byDate[symbols[i]];
                var b = byDate[symbols[j]];
                var dates = a.Keys.Where(b.ContainsKey).OrderDescending().Take(Math.Max(2, lookbackDays)).ToList();
                if (dates.Count < Math.Max(2, minCommon))
                {
                    continue;
                }

                var c = Pearson([.. dates.Select(d => a[d])], [.. dates.Select(d => b[d])]);
                if (c is { } value)
                {
                    pairs.Add((symbols[i], symbols[j], value));
                }
            }
        }

        return new CorrelationTable(pairs, underlying);
    }

    private static double? Pearson(double[] x, double[] y)
    {
        var mx = x.Average();
        var my = y.Average();
        double sxy = 0, sxx = 0, syy = 0;
        for (var i = 0; i < x.Length; i++)
        {
            var dx = x[i] - mx;
            var dy = y[i] - my;
            sxy += dx * dy;
            sxx += dx * dx;
            syy += dy * dy;
        }

        return sxx <= 0 || syy <= 0 ? null : sxy / Math.Sqrt(sxx * syy);
    }

    private static (string, string) Key(string a, string b) => string.CompareOrdinal(a, b) <= 0 ? (a, b) : (b, a);
}
