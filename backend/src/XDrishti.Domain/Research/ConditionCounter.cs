namespace XDrishti.Domain.Research;

/// <summary>
/// How often one condition of a strategy (its path in the model, <c>buy.trigger[0]</c>) was true, false and unknown at the steps where the scanner counted it (P3-03a, D10). <paramref name="NotReady"/> is an unknown result in which
/// at least one feature of the condition had no value at lag 0 (warm-up, a pending per-session feature); <paramref name="Unknown"/> is any other unknown (a lag the history cannot give, a division by zero). The four add up to the
/// steps the item was counted at. A plain value with pure sums.
/// </summary>
public sealed record ConditionCounter(string Path, long True, long False, long Unknown, long NotReady)
{
    /// <summary>The sum of the four counts: the steps at which the condition was evaluated and counted.</summary>
    public long Total => True + False + Unknown + NotReady;

    /// <summary>The counts of two runs of the same condition, added.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="other"/> is null.</exception>
    /// <exception cref="ArgumentException">The two counters are for different paths.</exception>
    public ConditionCounter Add(ConditionCounter other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (!string.Equals(Path, other.Path, StringComparison.Ordinal))
        {
            throw new ArgumentException("Counters of different conditions cannot be added: `" + Path + "` and `" + other.Path + "`.", nameof(other));
        }

        return new ConditionCounter(Path, True + other.True, False + other.False, Unknown + other.Unknown, NotReady + other.NotReady);
    }
}
