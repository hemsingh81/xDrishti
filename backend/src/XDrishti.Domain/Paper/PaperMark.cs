namespace XDrishti.Domain.Paper;

/// <summary>
/// The mark of an open position (spec P10-02, criterion 3): the close of the last closed session when the newest stored daily bar is exactly that, else <c>unavailable</c> with the reason and no price. A stale bar is never used as if it were current.
/// </summary>
public sealed record PaperMark
{
    private const string ValueState = "value";

    private const string UnavailableState = "unavailable";

    /// <summary>The state: value or unavailable.</summary>
    public string State { get; private init; } = UnavailableState;

    /// <summary>The close; null unless the state is value.</summary>
    public decimal? Price { get; private init; }

    /// <summary>The India date of the bar read; null when none exists.</summary>
    public DateOnly? AsOf { get; private init; }

    /// <summary>Why the mark is unavailable; null for a value.</summary>
    public string? Reason { get; private init; }

    /// <summary>The mark from the newest stored daily bar (null when none is stored) against the last closed session.</summary>
    public static PaperMark Of(DateOnly lastClosedSession, DailyClose? newest)
    {
        if (newest is null)
        {
            return new PaperMark { Reason = "no stored daily bar for this instrument" };
        }

        if (newest.Date != lastClosedSession)
        {
            return new PaperMark { AsOf = newest.Date, Reason = $"the newest stored daily bar is {newest.Date:yyyy-MM-dd}, not the last closed session {lastClosedSession:yyyy-MM-dd}" };
        }

        return newest.Complete
            ? new PaperMark { State = ValueState, Price = newest.Price, AsOf = newest.Date }
            : new PaperMark { AsOf = newest.Date, Reason = $"the daily bar of {newest.Date:yyyy-MM-dd} is not complete" };
    }
}
