namespace XDrishti.Domain.Market.Baskets;

/// <summary>The thresholds of the guard (the settings <c>Baskets:System:Fetch</c>).</summary>
public sealed record FetchLimits(decimal MaxChangeFraction, int ChangeFloor, decimal TripwireFraction, decimal MaxUnmatchedFraction)
{
    public static readonly FetchLimits Default = new(0.06m, 3, 0.5m, 0.2m);
}

/// <summary>What the guard looks at for one file.</summary>
public sealed record FetchFacts(
    bool HeaderOk, int Rows, int ExpectedMin, int ExpectedMax, int Unmatched, int CurrentMembers, int Added, int Removed, bool InReviewWindow, bool Forced);

/// <summary>The guard's verdict: ok, or the stable reason of the rejection.</summary>
public sealed record GuardVerdict(bool Ok, string? Reason)
{
    public static readonly GuardVerdict Pass = new(true, null);
}

/// <summary>Fail closed (BK-01 decision 4): header, row-count bounds, unmatched fraction, tripwire, change bound; in this order. Pure.</summary>
public static class FetchGuard
{
    public const string BadHeader = "bad_header";
    public const string RowCount = "row_count";
    public const string TooManyUnmatched = "unmatched";
    public const string Tripwire = "tripwire";
    public const string TooManyChanges = "too_many_changes";

    /// <summary>The largest change outside a review window: <c>max(floor, ceil(fraction x current))</c>.</summary>
    public static int AllowedChange(int current, FetchLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        return Math.Max(limits.ChangeFloor, (int)Math.Ceiling(limits.MaxChangeFraction * current));
    }

    public static GuardVerdict Check(FetchFacts facts, FetchLimits limits)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(limits);
        if (!facts.HeaderOk)
        {
            return new GuardVerdict(false, BadHeader);
        }

        if (facts.Rows < facts.ExpectedMin || facts.Rows > facts.ExpectedMax)
        {
            return new GuardVerdict(false, RowCount);
        }

        if (facts.Rows > 0 && (decimal)facts.Unmatched / facts.Rows > limits.MaxUnmatchedFraction)
        {
            return new GuardVerdict(false, TooManyUnmatched);
        }

        if (facts.CurrentMembers == 0)
        {
            return GuardVerdict.Pass; // the first fetch has nothing to compare with
        }

        if (!facts.Forced && (decimal)facts.Removed / facts.CurrentMembers > limits.TripwireFraction)
        {
            return new GuardVerdict(false, Tripwire);
        }

        return !facts.InReviewWindow && Math.Max(facts.Added, facts.Removed) > AllowedChange(facts.CurrentMembers, limits)
            ? new GuardVerdict(false, TooManyChanges)
            : GuardVerdict.Pass;
    }
}
