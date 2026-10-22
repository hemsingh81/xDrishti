using System.Globalization;
using XDrishti.Domain.Lab;

namespace XDrishti.Domain.Learning;

/// <summary>
/// The eligibility rule of P6-00 D4 (P6-05, ADR 0067), a pure function so that it has a hand table. A candidate beats a baseline when its log loss and its Brier score are both lower by more than the margin; the margins are multiplied by the
/// <see cref="MultipleTesting.Factor"/> of the trials. Eligible needs: both baselines beaten in at least <c>ceil(share * folds)</c> folds, both beaten on the pooled rows, a pooled calibration error within the limit and enough train rows.
/// Fewer used folds than the minimum turn an otherwise passing candidate into inconclusive.
/// </summary>
public static class EligibilityRule
{
    /// <summary>The verdict on one candidate.</summary>
    /// <param name="name">The candidate.</param>
    /// <param name="folds">One comparison per used fold.</param>
    /// <param name="pooled">The comparison over the pooled out-of-sample rows.</param>
    /// <param name="trainRows">All rows the final model would train on.</param>
    /// <param name="options">The margins and limits.</param>
    public static CandidateVerdict Judge(string name, IReadOnlyList<BaselineComparison> folds, BaselineComparison pooled, int trainRows, ValidationOptions options)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(folds);
        ArgumentNullException.ThrowIfNull(pooled);
        ArgumentNullException.ThrowIfNull(options);
        var factor = MultipleTesting.Factor(options.Trials);
        var logMargin = options.LogLossMargin * factor;
        var brierMargin = options.BrierMargin * factor;
        var needed = (int)Math.Ceiling((options.FoldsBetterShare * folds.Count) - 1e-9);
        var beating = folds.Count(f => BeatsBoth(f, logMargin, brierMargin));
        var pooledBeats = BeatsBoth(pooled, logMargin, brierMargin);
        var ece = pooled.Candidate.EcePp;
        var c = CultureInfo.InvariantCulture;

        var reasons = new List<string>();
        if (beating < needed)
        {
            reasons.Add(string.Create(c, $"beats both baselines in {beating} of {folds.Count} folds, {needed} needed"));
        }

        if (!pooledBeats)
        {
            reasons.Add(string.Create(c, $"does not beat both baselines on the pooled rows by log loss {logMargin:F4} and Brier {brierMargin:F4}"));
        }

        if (ece > options.MaxCalibrationErrorPp)
        {
            reasons.Add(string.Create(c, $"calibration error {ece:F2} pp is above {options.MaxCalibrationErrorPp:F2} pp"));
        }

        if (trainRows < options.MinTrainTrades)
        {
            reasons.Add(string.Create(c, $"trained on {trainRows} rows, {options.MinTrainTrades} needed"));
        }

        var outcome = ValidationOutcome.CannotBeatBaselines;
        if (reasons.Count == 0)
        {
            if (folds.Count < options.MinFoldsForVerdict)
            {
                outcome = ValidationOutcome.Inconclusive;
                reasons.Add(string.Create(c, $"only {folds.Count} folds, {options.MinFoldsForVerdict} needed to call it eligible"));
            }
            else
            {
                outcome = ValidationOutcome.Eligible;
            }
        }

        return new CandidateVerdict(name, outcome, beating, folds.Count, needed, pooledBeats, ece, reasons);
    }

    private static bool BeatsBoth(BaselineComparison c, double logMargin, double brierMargin) =>
        Beats(c.Candidate, c.CellRate, logMargin, brierMargin) && Beats(c.Candidate, c.Logistic, logMargin, brierMargin);

    private static bool Beats(ProbabilityMetrics candidate, ProbabilityMetrics baseline, double logMargin, double brierMargin) =>
        candidate.LogLoss < baseline.LogLoss - logMargin && candidate.Brier < baseline.Brier - brierMargin;
}
