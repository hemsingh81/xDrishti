namespace XDrishti.Application.Abstractions.OpeningBars;

/// <summary>What the derived job reads (BK-03b): the day's live bars, the history of the opening minutes (stored 1-minute bars and earlier live rows) and the day's events. No network.</summary>
public interface IOpeningBarsQueries
{
    ValueTask<IReadOnlyList<MinuteBar>> ReadDayAsync(DateOnly day, CancellationToken cancellationToken);

    /// <summary>
    /// The bars from 09:15 for <paramref name="minutes"/> minutes (IST) of every day from <paramref name="from"/> to <paramref name="to"/> (inclusive) for the instruments: stored <c>market.bars_1m</c> first, live rows only for a minute that has none there
    /// and never from a day in <paramref name="excludeLiveDays"/> (a day whose recording had a gap, a restart, low disk or a clock skew).
    /// </summary>
    ValueTask<IReadOnlyList<MinuteBar>> ReadHistoryAsync(IReadOnlyCollection<Guid> instruments, DateOnly from, DateOnly to, int minutes, IReadOnlyCollection<DateOnly> excludeLiveDays, CancellationToken cancellationToken);

    /// <summary>The events of every day from <paramref name="from"/> to <paramref name="to"/> (inclusive), by day.</summary>
    ValueTask<IReadOnlyDictionary<DateOnly, IReadOnlyList<OpeningBarsEvent>>> ReadEventsAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<OpeningBarsEvent>> ReadEventsAsync(DateOnly day, CancellationToken cancellationToken);
}
