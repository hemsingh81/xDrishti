using System.Globalization;

namespace XDrishti.Domain.Confidence;

/// <summary>
/// Builds the <see cref="DependenceAssessment"/> of a set of trades from their India entry dates and their R (CF-01). Pure and deterministic; the input order does not matter. Fewer trades than <see cref="ConfidenceOptions.MinTradesForEstimate"/> get nothing
/// but the reason; unweighted (recency weights change how much a trade counts, not how independent it is).
/// </summary>
public static class DependenceEstimator
{
    private const int MinForShape = 20;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <param name="trades">Entry date and R of each trade.</param>
    /// <param name="options">The settings; <see cref="ConfidenceOptions.Mode"/> is copied to the result.</param>
    /// <param name="minTrades">The trade minimum <c>EnoughByNEff</c> is judged against.</param>
    /// <param name="gateMinExpectancyR">The gate's minimum expectancy, the target edge when the options name none.</param>
    public static DependenceAssessment Assess(IReadOnlyList<(DateOnly Entry, double R)> trades, ConfidenceOptions options, int minTrades, double gateMinExpectancyR)
    {
        ArgumentNullException.ThrowIfNull(trades);
        ArgumentNullException.ThrowIfNull(options);
        var n = trades.Count;
        if (n < options.MinTradesForEstimate)
        {
            return Unavailable(options, n, $"n < {options.MinTradesForEstimate.ToString(Inv)}", options.EdgeOr(gateMinExpectancyR));
        }

        if (trades.Any(t => !double.IsFinite(t.R)))
        {
            return Unavailable(options, n, "a result is not a finite number", options.EdgeOr(gateMinExpectancyR));
        }

        var notes = new List<string>();
        var r = trades.Select(t => t.R).ToArray();
        var mean = r.Average();
        var sd = Math.Sqrt(r.Sum(v => (v - mean) * (v - mean)) / (n - 1));
        var sigma = PowerAnalysis.Sigma(sd, options.SigmaFloorR);
        var needed = PowerAnalysis.TradesNeeded(sigma, options.EdgeOr(gateMinExpectancyR), options.Alpha, options.Power);

        double skew = 0;
        double kurt = 3;
        var normalAssumed = n < MinForShape;
        if (!normalAssumed && sd > 0)
        {
            var m2 = r.Sum(v => Math.Pow(v - mean, 2)) / n;
            skew = r.Sum(v => Math.Pow(v - mean, 3)) / n / Math.Pow(m2, 1.5);
            kurt = r.Sum(v => Math.Pow(v - mean, 4)) / n / (m2 * m2);
        }

        int? minTrl = null;
        if (sd > 0)
        {
            minTrl = MinTrackRecord.Trades(mean / sd, options.BenchmarkSr, skew, kurt, options.Confidence);
        }

        if (minTrl is null)
        {
            notes.Add(sd <= 0
                ? "minTrl unavailable: no spread in R"
                : mean / sd <= options.BenchmarkSr ? "minTrl unavailable: no edge to detect" : "minTrl unavailable: the skew and kurtosis term is not positive");
        }
        else if (normalAssumed)
        {
            notes.Add("normal assumed for minTrl (fewer than 20 trades)");
        }

        var clusterTrades = trades.Select(t => (t.Entry.ToString("yyyy-MM-dd", Inv), t.R)).ToList();
        var daily = trades.GroupBy(t => t.Entry).OrderBy(g => g.Key).Select(g => g.Average(t => t.R)).ToList();
        var estimate = EffectiveSampleSize.Combine(EffectiveSampleSize.Cluster(clusterTrades, options.MinClusters), EffectiveSampleSize.Serial(daily, options.MinSessionsForAutocorr, n));
        if (!estimate.IsAvailable)
        {
            notes.Add(estimate.Note);
        }
        else if (estimate.Note.Length > 0)
        {
            notes.Add(estimate.Note);
        }

        if (n < MinForShape)
        {
            notes.Add("fewer than 20 trades: the correlation estimates are noisy");
        }

        var span = (trades.Max(t => t.Entry).DayNumber - trades.Min(t => t.Entry).DayNumber + 1) / 365.25;
        var nEff = estimate.NEff;
        return new DependenceAssessment(
            options.Mode,
            n,
            nEff,
            estimate.DesignEffect,
            estimate.Method,
            needed,
            nEff.HasValue ? PowerAnalysis.MinDetectableEdge(sigma, nEff.Value, options.Alpha, options.Power) : null,
            minTrl,
            minTrl.HasValue && estimate.DesignEffect.HasValue ? (int)Math.Ceiling(Math.Round(minTrl.Value * estimate.DesignEffect.Value, 9)) : null,
            span,
            nEff.HasValue && needed.HasValue ? PowerAnalysis.YearsToEvidence(nEff.Value, span, needed.Value) : null,
            nEff.HasValue ? CeilN(nEff.Value) >= minTrades : null,
            normalAssumed,
            string.Join("; ", notes),
            options.EdgeOr(gateMinExpectancyR),
            options.KeyText());
    }

    /// <summary>Effective trades rounded up to a whole number (9 decimals first, so a float error does not add one).</summary>
    public static int CeilN(double nEff) => (int)Math.Ceiling(Math.Round(nEff, 9));

    private static DependenceAssessment Unavailable(ConfidenceOptions options, int n, string reason, double edge) =>
        new(options.Mode, n, null, null, EffectiveEstimate.NoneMethod, null, null, null, null, null, null, null, false, "unavailable: " + reason, edge, options.KeyText());
}
