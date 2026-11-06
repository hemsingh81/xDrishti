namespace XDrishti.Domain.Readiness;

/// <summary>
/// The system level of the readiness (spec CF-11 D5): <c>system = min(integrity, capital-weighted mean of the strategy scores)</c>, the weights being the planned risk shares, or equal (and labelled) when the plan exposes no risk share for every strategy. Integrity is 100 when
/// clear and 0 when a check fails or cannot be read (unavailable is never a pass). Pure.
/// </summary>
public static class SystemReadinessCalculator
{
    /// <summary>Combines the strategies.</summary>
    public static SystemReadiness Compute(IReadOnlyList<StrategyReadiness> strategies, FaultState integrity, ReadinessOptions options)
    {
        ArgumentNullException.ThrowIfNull(strategies);
        ArgumentNullException.ThrowIfNull(options);
        var integrityScore = integrity is FaultState.Clear or FaultState.NotApplicable ? 100.0 : 0.0;
        if (strategies.Count == 0)
        {
            return new SystemReadiness(0, ReadinessLabel.NotReady, integrityScore, 0, true, null, null, 0, "no strategy yet");
        }

        var shares = strategies.Select(s => s.RiskShare).ToList();
        var useShares = shares.All(s => s is { } v && double.IsFinite(v) && v >= 0) && shares.Sum(s => s!.Value) > 0;
        var weights = useShares ? shares.Select(s => s!.Value).ToList() : [.. strategies.Select(_ => 1.0)];
        var total = weights.Sum();
        var mean = 0.0;
        for (var i = 0; i < strategies.Count; i++)
        {
            mean += weights[i] / total * strategies[i].Score;
        }

        var weakest = strategies.OrderBy(s => s.Score).ThenBy(s => s.Strategy, StringComparer.Ordinal).First();
        var score = Math.Min(integrityScore, mean);
        var reason = integrityScore < 100 ? "an integrity check failed or cannot be read" : null;
        return new SystemReadiness(score, ReadinessLabels.Of(score, options), integrityScore, mean, !useShares, weakest.Strategy, weakest.WeakestSubScore, strategies.Count, reason);
    }
}
