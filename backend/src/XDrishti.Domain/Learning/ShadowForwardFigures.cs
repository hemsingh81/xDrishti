namespace XDrishti.Domain.Learning;

/// <summary>
/// The forward figures of a model on its graded shadow candidates (CF-10e part 10e-1, spec D3). Pure and deterministic. Candidates are sorted by probability, highest first, ties by trade key in ordinal order; the top and the bottom floor(n / 2) are compared (with an odd n the
/// middle one is left out). The rolling calibration error is the existing ECE of the newest <see cref="EceWindow"/> outcomes (all of them when fewer). A win is an R above zero.
/// </summary>
public static class ShadowForwardFigures
{
    /// <summary>Outcomes in the rolling calibration window.</summary>
    public const int EceWindow = 100;

    /// <summary>The figures, or null when there is no outcome (nothing is available).</summary>
    public static MlForwardFacts? Compute(IReadOnlyList<ScoredOutcome> outcomes)
    {
        ArgumentNullException.ThrowIfNull(outcomes);
        if (outcomes.Count == 0)
        {
            return null;
        }

        var n = outcomes.Count;
        var first = outcomes.Min(o => o.SessionDate);
        var last = outcomes.Max(o => o.SessionDate);
        var weeks = (last.DayNumber - first.DayNumber) / 7.0;

        var half = n / 2;
        double? spread = null;
        if (half > 0)
        {
            var ranked = outcomes.OrderByDescending(o => o.PWin).ThenBy(o => o.TradeKey, StringComparer.Ordinal).ToList();
            spread = ranked.Take(half).Average(o => o.R) - ranked.Skip(n - half).Average(o => o.R);
        }

        var recent = outcomes.OrderBy(o => o.SessionDate).ThenBy(o => o.TradeKey, StringComparer.Ordinal).TakeLast(EceWindow).ToList();
        var ece = MetricsCalculator.EcePp([.. recent.Select(o => o.PWin)], [.. recent.Select(o => o.R > 0 ? 1 : 0)]);
        return new MlForwardFacts(n, weeks, spread, ece);
    }
}
