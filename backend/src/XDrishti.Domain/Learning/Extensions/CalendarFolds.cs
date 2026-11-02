using System.Globalization;

namespace XDrishti.Domain.Learning.Extensions;

/// <summary>
/// Calendar test folds of the comparison (P13-05, decision 4): a month (P13) or a calendar quarter (P15), cut by this code and not by the P6-05 geometry. The first window starts on the first unit boundary at least <c>MinTrainMonths</c> after the first row date. A window is used only when the caller's
/// session list reaches its last day (an incomplete last window is dropped and counted). Train rows are the rows before the window minus the embargo (the rows of the last <c>EmbargoSessions</c> sessions before the window) and minus the purge (resolved instant plus one bar not before the window's first decision).
/// </summary>
public static class CalendarFolds
{
    /// <summary>Cuts the folds.</summary>
    /// <param name="ordered">All rows, ordered by trade date, decision instant and key; unique keys.</param>
    /// <param name="sessions">The trading sessions, ascending and distinct.</param>
    /// <param name="options">The knobs.</param>
    public static CalendarFoldSet Cut(IReadOnlyList<ValidationRow> ordered, IReadOnlyList<DateOnly> sessions, ExtensionComparisonOptions options)
    {
        ArgumentNullException.ThrowIfNull(ordered);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(options);
        var folds = new List<CalendarFold>();
        if (ordered.Count == 0)
        {
            return new CalendarFoldSet(folds, 0);
        }

        var last = ordered[^1].TradeDate;
        var dropped = 0;
        var start = Boundary(ordered[0].TradeDate.AddMonths(options.MinTrainMonths), options.Unit);
        while (start <= last)
        {
            var end = EndOf(start, options.Unit);
            if (sessions.Count == 0 || sessions[^1] < end)
            {
                dropped++;
                start = end.AddDays(1);
                continue;
            }

            folds.Add(Window(ordered, sessions, options, start, end));
            start = end.AddDays(1);
        }

        return new CalendarFoldSet(folds, dropped);
    }

    /// <summary>The first unit boundary on or after <paramref name="date"/>.</summary>
    internal static DateOnly Boundary(DateOnly date, FoldUnit unit)
    {
        var first = new DateOnly(date.Year, date.Month, 1);
        if (unit == FoldUnit.Month)
        {
            return date.Day == 1 ? date : first.AddMonths(1);
        }

        var quarterStart = new DateOnly(date.Year, ((date.Month - 1) / 3 * 3) + 1, 1);
        return date == quarterStart ? date : quarterStart.AddMonths(3);
    }

    /// <summary>The last day of the unit that starts on <paramref name="start"/>.</summary>
    internal static DateOnly EndOf(DateOnly start, FoldUnit unit) => start.AddMonths(unit == FoldUnit.Month ? 1 : 3).AddDays(-1);

    private static CalendarFold Window(IReadOnlyList<ValidationRow> ordered, IReadOnlyList<DateOnly> sessions, ExtensionComparisonOptions options, DateOnly start, DateOnly end)
    {
        var test = ordered.Where(r => r.TradeDate >= start && r.TradeDate <= end).ToArray();
        if (test.Length == 0)
        {
            return new CalendarFold(start, end, [], [], "no rows in the window");
        }

        var embargoFrom = EmbargoFrom(sessions, start, options.EmbargoSessions);
        var firstDecision = test.Min(r => r.DecisionEpochSeconds);
        var train = ordered.Where(r => r.TradeDate < embargoFrom && r.ResolvedEpochSeconds + HoldOut.BarSeconds < firstDecision).ToArray();
        return train.Length < options.MinTrainTrades
            ? new CalendarFold(start, end, train, test, string.Create(CultureInfo.InvariantCulture, $"{train.Length} train rows, {options.MinTrainTrades} needed"))
            : new CalendarFold(start, end, train, test, null);
    }

    /// <summary>The date of the earliest of the last <paramref name="count"/> sessions before <paramref name="start"/>; train rows are dated before it. With no embargo (or no earlier session) it is <paramref name="start"/>.</summary>
    internal static DateOnly EmbargoFrom(IReadOnlyList<DateOnly> sessions, DateOnly start, int count)
    {
        var before = LowerBound(sessions, start);
        return count == 0 || before == 0 ? start : sessions[Math.Max(0, before - count)];
    }

    /// <summary>The number of sessions dated before <paramref name="date"/> in the ascending list.</summary>
    internal static int LowerBound(IReadOnlyList<DateOnly> sessions, DateOnly date)
    {
        int lo = 0, hi = sessions.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (sessions[mid] < date)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return lo;
    }
}
