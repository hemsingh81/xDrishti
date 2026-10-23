namespace XDrishti.Domain.Selection.SmartRules;

/// <summary>
/// The correlations the smart rules read: a <see cref="CorrelationMatrix"/> (one implementation of pair lookup and symbol normalisation) plus optional "same underlying" groups (spec P7-06).
/// A pair with no figure is unknown, never zero.
/// </summary>
public sealed class CorrelationTable
{
    private readonly CorrelationMatrix _matrix;
    private readonly Dictionary<string, string> _underlying = [];

    /// <summary>Creates a table.</summary>
    /// <param name="pairs">Correlations by symbol pair, in either order.</param>
    /// <param name="underlying">Symbol to underlying group; two symbols in the same group are the same bet (same side) without a figure.</param>
    public CorrelationTable(IEnumerable<(string A, string B, double Correlation)> pairs, IReadOnlyDictionary<string, string>? underlying = null)
        : this(new CorrelationMatrix(pairs ?? throw new ArgumentNullException(nameof(pairs))), underlying)
    {
    }

    /// <summary>Creates a table over an existing matrix.</summary>
    public CorrelationTable(CorrelationMatrix matrix, IReadOnlyDictionary<string, string>? underlying = null)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        _matrix = matrix;
        foreach (var (symbol, group) in underlying ?? new Dictionary<string, string>())
        {
            _underlying[CorrelationMatrix.Normalize(symbol)] = CorrelationMatrix.Normalize(group);
        }
    }

    /// <summary>The correlation of two symbols, or null when unknown.</summary>
    public double? Get(string a, string b) => _matrix.Get(a, b);

    /// <summary>True when both symbols are in the same underlying group.</summary>
    public bool SameUnderlying(string a, string b) =>
        _underlying.TryGetValue(CorrelationMatrix.Normalize(a), out var ga) && _underlying.TryGetValue(CorrelationMatrix.Normalize(b), out var gb) && ga == gb;

    /// <summary>
    /// Builds the table from daily returns: Pearson correlation of the last <paramref name="lookbackDays"/> common sessions strictly before <paramref name="decisionDate"/>; a pair with fewer than <paramref name="minCommon"/> common sessions is unknown.
    /// Only pairs the plan needs are computed: candidate with candidate and candidate with a book symbol. A symbol with two different returns on one date has that date dropped (ambiguous, so the result does not depend on input order).
    /// </summary>
    public static CorrelationTable FromReturns(
        IReadOnlyDictionary<string, IReadOnlyList<DailyReturn>> returns,
        DateOnly decisionDate,
        IReadOnlyCollection<string> candidates,
        IReadOnlyCollection<string>? book = null,
        int lookbackDays = 60,
        int minCommon = 30,
        IReadOnlyDictionary<string, string>? underlying = null)
    {
        ArgumentNullException.ThrowIfNull(returns);
        ArgumentNullException.ThrowIfNull(candidates);
        var series = new Dictionary<string, Dictionary<DateOnly, double>>();
        foreach (var (symbol, list) in returns)
        {
            var key = CorrelationMatrix.Normalize(symbol);
            var byDate = list
                .Where(r => r.Date < decisionDate && double.IsFinite(r.Value))
                .GroupBy(r => r.Date)
                .Where(g => g.Select(r => r.Value).Distinct().Count() == 1)
                .ToDictionary(g => g.Key, g => g.First().Value);
            series[key] = series.TryGetValue(key, out var existing) ? Merge(existing, byDate) : byDate;
        }

        var cands = candidates.Select(CorrelationMatrix.Normalize).Distinct().Order(StringComparer.Ordinal).ToList();
        var others = cands.Concat((book ?? []).Select(CorrelationMatrix.Normalize)).Distinct().Order(StringComparer.Ordinal).ToList();
        var pairs = new List<(string, string, double)>();
        foreach (var a in cands)
        {
            foreach (var b in others.Where(o => !string.Equals(o, a, StringComparison.Ordinal) && !(cands.Contains(o) && string.CompareOrdinal(o, a) < 0)))
            {
                if (!series.TryGetValue(a, out var sa) || !series.TryGetValue(b, out var sb))
                {
                    continue;
                }

                var dates = sa.Keys.Where(sb.ContainsKey).OrderDescending().Take(Math.Max(2, lookbackDays)).ToList();
                if (dates.Count < Math.Max(2, minCommon))
                {
                    continue;
                }

                if (Pearson([.. dates.Select(d => sa[d])], [.. dates.Select(d => sb[d])]) is { } c)
                {
                    pairs.Add((a, b, c));
                }
            }
        }

        return new CorrelationTable(pairs, underlying);
    }

    // Two entries for one normalised symbol: keep only the dates on which they agree.
    private static Dictionary<DateOnly, double> Merge(Dictionary<DateOnly, double> x, Dictionary<DateOnly, double> y) =>
        x.Where(kv => !y.TryGetValue(kv.Key, out var other) || other == kv.Value)
            .Concat(y.Where(kv => !x.ContainsKey(kv.Key)))
            .ToDictionary(kv => kv.Key, kv => kv.Value);

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
}
