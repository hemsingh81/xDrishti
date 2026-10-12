namespace XDrishti.Domain.Market.Bars;

/// <summary>
/// One session of one instrument with fewer 1-minute bars than it should have (ADR 0017 §3, <c>market.bar_gaps</c>). <see cref="Expected"/> comes from the trading
/// calendar (375, or the length of a special session), <see cref="Found"/> is what is stored. A refetch decides what happens next: all bars there →
/// <see cref="GapStatus.Filled"/>; some more bars → still <see cref="GapStatus.Open"/>; none more → <see cref="GapStatus.Unfillable"/>.
/// </summary>
public sealed class BarGap
{
    // Parameter names match property names so EF Core can bind this constructor when materialising.
    private BarGap(Guid instrumentId, DateOnly date, int expected, int found, int attempts, DateTimeOffset? lastAttemptAt, GapStatus status, DateTimeOffset detectedAt)
    {
        InstrumentId = instrumentId;
        Date = date;
        Expected = expected;
        Found = found;
        Attempts = attempts;
        LastAttemptAt = lastAttemptAt;
        Status = status;
        DetectedAt = detectedAt;
    }

    public Guid InstrumentId { get; private set; }

    /// <summary>The India date of the session.</summary>
    public DateOnly Date { get; private set; }

    public int Expected { get; private set; }

    public int Found { get; private set; }

    /// <summary>How many times a refetch was made for it.</summary>
    public int Attempts { get; private set; }

    public DateTimeOffset? LastAttemptAt { get; private set; }

    public GapStatus Status { get; private set; }

    /// <summary>When the gap was first found (found again after it was filled counts as a new one).</summary>
    public DateTimeOffset DetectedAt { get; private set; }

    /// <summary>Whether a session with <paramref name="found"/> bars of <paramref name="expected"/> is complete.</summary>
    public static bool IsComplete(int expected, int found) => found >= expected;

    /// <summary>A newly found gap, or a day already complete when <paramref name="found"/> reaches <paramref name="expected"/> (then it is recorded as filled).</summary>
    public static BarGap Detect(Guid instrumentId, DateOnly date, int expected, int found, DateTimeOffset now)
    {
        Check(expected, found);
        return new BarGap(instrumentId, date, expected, found, 0, null, IsComplete(expected, found) ? GapStatus.Filled : GapStatus.Open, now);
    }

    /// <summary>
    /// Brings the gap up to date with what is stored now, without a refetch: complete → filled; a filled day that lost bars (a file was undone) → open again, with
    /// no attempts; otherwise only the counts change (an unfillable day stays unfillable). Returns false when nothing changed.
    /// </summary>
    public bool Refresh(int expected, int found, DateTimeOffset now)
    {
        Check(expected, found);
        var status = IsComplete(expected, found) ? GapStatus.Filled : Status == GapStatus.Filled ? GapStatus.Open : Status;
        if (Expected == expected && Found == found && Status == status)
        {
            return false;
        }

        if (Status == GapStatus.Filled && status == GapStatus.Open)
        {
            Attempts = 0;
            LastAttemptAt = null;
            DetectedAt = now;
        }

        Expected = expected;
        Found = found;
        Status = status;
        return true;
    }

    /// <summary>
    /// Records a refetch of the day: it had <paramref name="foundBefore"/> bars before and has <paramref name="foundAfter"/> now. All there → filled; more but not all →
    /// open with the new count; no more → unfillable.
    /// </summary>
    public void RecordRefetch(int foundBefore, int foundAfter, DateTimeOffset now)
    {
        Check(Expected, foundAfter);
        Attempts++;
        LastAttemptAt = now;
        Found = foundAfter;
        Status = IsComplete(Expected, foundAfter) ? GapStatus.Filled : foundAfter > foundBefore ? GapStatus.Open : GapStatus.Unfillable;
    }

    private static void Check(int expected, int found)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(expected);
        ArgumentOutOfRangeException.ThrowIfNegative(found);
    }
}
