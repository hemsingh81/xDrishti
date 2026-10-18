namespace XDrishti.Domain.Research;

/// <summary>
/// What one entry scanner (one instrument, one side) counted (spec P3-03a, D10): the bars it was given, the decision steps and what became of them, the fate of every eligible step, and per condition the true, false, unknown
/// and not-ready counts. A rule that never fires gives zero candidates and looks safe; these counters are how it is seen. Plain values with pure sums, so that the run record can add the counters of many instruments. Two
/// counters are equal when every figure and every condition counter, in order, is equal.
/// <para>
/// Identities (checked by the tests on every fixture): <c>DecisionSteps = StaleBucketSteps + OutOfWindowSteps + WindowSteps</c>; the eligible steps add up to
/// <c>Candidates + LaterTriggers + BlockedByInvalidation + UnknownInvalidation + StopNotReady</c>; the four counts of a setup or trigger condition add up to <c>WindowSteps</c>, those of an invalidate condition to
/// <c>DecisionSteps − StaleBucketSteps</c>. <c>FullyReadySteps</c> are the window steps at which every feature of every setup and trigger condition had a value at lag 0, whatever the result of the condition.
/// </para>
/// </summary>
public sealed record ScanCounters(
    Side Side,
    long Bars,
    long IgnoredBars,
    long IncompleteBarsConsumed,
    long DecisionSteps,
    long LostDecisions,
    long StaleBucketSteps,
    long OutOfWindowSteps,
    long WindowSteps,
    long FullyReadySteps,
    long Candidates,
    long LaterTriggers,
    long BlockedByInvalidation,
    long InvalidatedDays,
    long StopNotReady,
    long UnknownInvalidation,
    IReadOnlyList<ConditionCounter> Conditions)
{
    /// <summary>The counters of a side that has seen nothing.</summary>
    public static ScanCounters Empty(Side side) => new(side, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, []);

    /// <summary>The eligible steps: window steps at which setup and trigger were true (the sum of the fates of D2 step 4).</summary>
    public long EligibleSteps => Candidates + LaterTriggers + BlockedByInvalidation + UnknownInvalidation + StopNotReady;

    /// <summary>The figures of two runs of the same side, added; conditions are merged by path in the order first seen (this counter's, then the new ones of <paramref name="other"/>).</summary>
    /// <exception cref="ArgumentNullException"><paramref name="other"/> is null.</exception>
    /// <exception cref="ArgumentException">The two counters are for different sides.</exception>
    public ScanCounters Add(ScanCounters other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (Side != other.Side)
        {
            throw new ArgumentException("Counters of different sides cannot be added: " + Side + " and " + other.Side + ".", nameof(other));
        }

        var merged = new List<ConditionCounter>(Conditions);
        foreach (var condition in other.Conditions)
        {
            var at = merged.FindIndex(c => string.Equals(c.Path, condition.Path, StringComparison.Ordinal));
            if (at >= 0)
            {
                merged[at] = merged[at].Add(condition);
            }
            else
            {
                merged.Add(condition);
            }
        }

        return new ScanCounters(
            Side,
            Bars + other.Bars,
            IgnoredBars + other.IgnoredBars,
            IncompleteBarsConsumed + other.IncompleteBarsConsumed,
            DecisionSteps + other.DecisionSteps,
            LostDecisions + other.LostDecisions,
            StaleBucketSteps + other.StaleBucketSteps,
            OutOfWindowSteps + other.OutOfWindowSteps,
            WindowSteps + other.WindowSteps,
            FullyReadySteps + other.FullyReadySteps,
            Candidates + other.Candidates,
            LaterTriggers + other.LaterTriggers,
            BlockedByInvalidation + other.BlockedByInvalidation,
            InvalidatedDays + other.InvalidatedDays,
            StopNotReady + other.StopNotReady,
            UnknownInvalidation + other.UnknownInvalidation,
            merged.AsReadOnly());
    }

    /// <summary>Value equality: every figure, and the conditions in order.</summary>
    public bool Equals(ScanCounters? other) =>
        other is not null
        && Side == other.Side && Bars == other.Bars && IgnoredBars == other.IgnoredBars && IncompleteBarsConsumed == other.IncompleteBarsConsumed
        && DecisionSteps == other.DecisionSteps && LostDecisions == other.LostDecisions && StaleBucketSteps == other.StaleBucketSteps
        && OutOfWindowSteps == other.OutOfWindowSteps && WindowSteps == other.WindowSteps && FullyReadySteps == other.FullyReadySteps
        && Candidates == other.Candidates && LaterTriggers == other.LaterTriggers && BlockedByInvalidation == other.BlockedByInvalidation
        && InvalidatedDays == other.InvalidatedDays && StopNotReady == other.StopNotReady && UnknownInvalidation == other.UnknownInvalidation
        && Conditions.SequenceEqual(other.Conditions);

    /// <summary>A hash consistent with <see cref="Equals(ScanCounters?)"/>.</summary>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Side);
        hash.Add(Bars);
        hash.Add(DecisionSteps);
        hash.Add(WindowSteps);
        hash.Add(Candidates);
        foreach (var condition in Conditions)
        {
            hash.Add(condition);
        }

        return hash.ToHashCode();
    }
}
