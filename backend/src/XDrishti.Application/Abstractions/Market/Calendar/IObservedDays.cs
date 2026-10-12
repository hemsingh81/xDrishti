namespace XDrishti.Application.Abstractions.Market.Calendar;

/// <summary>
/// One date of the stored daily bars: how many instruments have any bar on it, how many of those have bars on both sides of it (not their first or last day),
/// and the median number of 1-minute bars among them.
/// </summary>
public sealed record DayCount(DateOnly Date, int Instruments, int Inside, int MedianBars);

/// <summary>The first and the last date an instrument has bars on.</summary>
public readonly record struct InstrumentSpan(DateOnly First, DateOnly Last);

/// <summary>
/// What the stored bars say about the days (from the daily aggregates). <see cref="Pending"/>: some instrument has changes that are not aggregated yet, so the
/// daily rows are not up to date and nothing can be concluded.
/// </summary>
public sealed record DayPresence(bool Pending, IReadOnlyList<DayCount> Days, IReadOnlyList<InstrumentSpan> Spans);

/// <summary>The session one date's bars lie in: the window most instruments share (first bar to the minute after the last bar, minutes since midnight India time) and how many instruments have bars that day.</summary>
public sealed record ObservedWindow(int Instruments, int FromMinute, int ToMinute);

/// <summary>Reads of the stored bars that the calendar's observed proposals (ADR 0017 §1) are derived from.</summary>
public interface IObservedDays
{
    ValueTask<DayPresence> ReadPresenceAsync(CancellationToken cancellationToken);

    /// <summary>The window of each of the dates that has bars (a date without bars is missing from the answer).</summary>
    ValueTask<IReadOnlyDictionary<DateOnly, ObservedWindow>> ReadWindowsAsync(IReadOnlyCollection<DateOnly> dates, CancellationToken cancellationToken);
}
