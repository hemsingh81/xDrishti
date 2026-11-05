using XDrishti.Domain.Confidence;

namespace XDrishti.Domain.Learning;

/// <summary>
/// "Is the learning loop helping?" (spec CF-12, D4; ADR 0218). Three arms pick the same candidates, since ML is advisory, and differ only in the take rule applied afterwards: A the frozen champion of the epoch's start, B the champion now, C no model (take every
/// candidate). For A and B a candidate is taken when its score is at or above that arm's own train base rate (the P6-12 rule; no searched threshold); an arm's value for a candidate is its R when taken and 0 when skipped. The paired difference per candidate is
/// resampled by whole trade day with the seeded <see cref="PairedDayBootstrap"/> of CF-10 (not copied). A versus B and B versus C are reported separately (review fix 2). The common set is the candidates the retrained arm scored; arm A is compared only when it
/// scored every one of them (a different set would be a different question). The first arm "helps" when it is better with probability at or above the setting <c>HelpsAt</c> and "hurts" when it is worse with probability at or above the fixed bar <see cref="LoopOptions.HurtsBar"/> of 0.95 (the same cut as P(better) at or below
/// 0.05, except that resamples that tie at exactly zero, for example two arms that took the same candidates, are never read as harm). Fewer than the minimum of distinct days is "inconclusive". Pure and seeded.
/// </summary>
public static class ThreeArmLoopTest
{
    /// <summary>Runs the three arms. <paramref name="seedKey"/> makes the bootstrap repeat (for example the strategy id, the champions' hashes and the span).</summary>
    public static ThreeArmResult Evaluate(IReadOnlyList<LoopCandidate> candidates, double? frozenBaseRate, double? retrainedBaseRate, LoopOptions options, string seedKey)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(seedKey);
        if (options.Problems().Count > 0)
        {
            throw new ArgumentException("The loop options are out of range: " + string.Join("; ", options.Problems()), nameof(options));
        }

        var common = candidates.Where(c => c.RetrainedScore is { } s && double.IsFinite(s) && double.IsFinite(c.R)).ToList();
        if (retrainedBaseRate is not { } baseB || !double.IsFinite(baseB) || common.Count == 0)
        {
            var why = common.Count == 0 ? "no candidate has a stored score of the current champion" : "the current champion's train base rate is not known";
            return new ThreeArmResult(common.Count, null, null, null, ArmComparison.Unavailable("retrained_vs_frozen", why), ArmComparison.Unavailable("retrained_vs_no_ml", why));
        }

        var b = common.Select(c => Take(c.RetrainedScore!.Value, baseB) ? c.R : 0).ToArray();
        var none = common.Select(c => c.R).ToArray();
        var days = common.Select(c => c.Day).ToArray();
        var versusNone = Compare("retrained_vs_no_ml", b, none, days, options.MinDays, options.Draws, options.Level, options.HelpsAt, LoopOptions.HurtsBar, seedKey + "|b-c");
        double? frozenMean = null;
        ArmComparison versusFrozen;
        if (frozenBaseRate is not { } baseA || !double.IsFinite(baseA))
        {
            versusFrozen = ArmComparison.Unavailable("retrained_vs_frozen", "the frozen champion's train base rate is not known", common.Count);
        }
        else if (common.Any(c => c.FrozenScore is not { } f || !double.IsFinite(f)))
        {
            versusFrozen = ArmComparison.Unavailable("retrained_vs_frozen", "the frozen champion could not score every candidate of the common set", common.Count);
        }
        else
        {
            var a = common.Select(c => Take(c.FrozenScore!.Value, baseA) ? c.R : 0).ToArray();
            frozenMean = a.Average();
            versusFrozen = Compare("retrained_vs_frozen", b, a, days, options.MinDays, options.Draws, options.Level, options.HelpsAt, LoopOptions.HurtsBar, seedKey + "|b-a");
        }

        return new ThreeArmResult(common.Count, frozenMean, b.Average(), none.Average(), versusFrozen, versusNone);
    }

    /// <summary>The take rule of an arm: the score at or above the arm's own train base rate.</summary>
    public static bool Take(double score, double baseRate) => score >= baseRate;

    /// <summary>Compares the values of a first and a second arm on the same candidates (same length, same order): the day bootstrap of the differences first minus second.</summary>
    public static ArmComparison Compare(string name, IReadOnlyList<double> first, IReadOnlyList<double> second, IReadOnlyList<DateOnly> days, int minDays, int draws, double level, double helpsAt, double hurtsBar, string seedKey)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        ArgumentNullException.ThrowIfNull(days);
        if (first.Count != second.Count || first.Count != days.Count || first.Count == 0)
        {
            return ArmComparison.Unavailable(name, "the arms must be given on the same non-empty set of candidates");
        }

        var gains = new double[first.Count];
        var mirrored = new double[first.Count];
        for (var i = 0; i < gains.Length; i++)
        {
            gains[i] = first[i] - second[i];
            mirrored[i] = -gains[i];
        }

        var better = PairedDayBootstrap.Assess(gains, days, draws, level, seedKey, minDays);
        if (better.IsFailure)
        {
            return better.Error.Code == PairedDayBootstrap.TooFewDaysCode
                ? new ArmComparison(name, LoopVerdict.Inconclusive, first.Count, days.Distinct().Count(), null, null, null, null, null, $"{name}: inconclusive - {days.Distinct().Count()} distinct days, {minDays} are needed")
                : ArmComparison.Unavailable(name, better.Error.Message, first.Count);
        }

        var worse = PairedDayBootstrap.Assess(mirrored, days, draws, level, seedKey, minDays);
        if (worse.IsFailure)
        {
            return ArmComparison.Unavailable(name, worse.Error.Message, first.Count);
        }

        var r = better.Value;
        var pWorse = worse.Value.ProbabilityBetter;
        var verdict = r.ProbabilityBetter >= helpsAt ? LoopVerdict.Helps : pWorse >= hurtsBar ? LoopVerdict.Hurts : LoopVerdict.NoMeasurableDifference;
        return new ArmComparison(name, verdict, r.Rows, r.Days, r.MeanGain, r.GainLow, r.GainHigh, r.ProbabilityBetter, pWorse, ArmComparison.Describe(name, verdict, r.MeanGain, r.GainLow, r.GainHigh, r.ProbabilityBetter, pWorse, r.Days, r.Level));
    }
}
