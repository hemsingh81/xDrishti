namespace XDrishti.Execution.Application.Halts;

/// <summary>The end-of-session mark of one account: the realised result, the marked (realised plus open) result of the cumulative curve since execution began, the capital and the peak of the curve so far.</summary>
public sealed record DayMark(Guid AccountId, DateOnly SessionDate, decimal Realised, decimal Marked, decimal Capital, decimal Peak, DateTimeOffset At);

/// <summary>Insert-only: one mark per account and session; a second write for the same day changes nothing and says so.</summary>
public interface IDayMarkStore
{
    /// <summary>False when the account already has a mark for that session.</summary>
    Task<bool> AppendAsync(DayMark mark, CancellationToken cancellationToken);

    Task<IReadOnlyList<DayMark>> LatestAsync(Guid accountId, int count, CancellationToken cancellationToken);
}

/// <summary>The drawdown reads the marks (spec criterion 11): a missing mark makes it unavailable, never zero.</summary>
public static class DayMarks
{
    /// <summary>The peak to store with a new mark: the larger of the previous peak and the new marked value (the curve starts at 0, so a first peak is never below 0).</summary>
    public static decimal NextPeak(decimal? previousPeak, decimal marked) => Math.Max(Math.Max(previousPeak ?? 0m, 0m), marked);

    /// <summary>The newest mark must be the one of <paramref name="lastSession"/> (the last session that should have ended); anything older or no mark at all is unavailable and the drawdown trigger does not fire on it.</summary>
    public static TriggerVerdict Drawdown(IReadOnlyList<DayMark> newestFirst, DateOnly lastSession, decimal pct)
    {
        ArgumentNullException.ThrowIfNull(newestFirst);
        return newestFirst.Count == 0 || newestFirst[0].SessionDate != lastSession
            ? TriggerVerdict.Unavailable
            : TriggerEvaluator.Drawdown(newestFirst[0].Peak, newestFirst[0].Marked, newestFirst[0].Capital, pct);
    }
}
