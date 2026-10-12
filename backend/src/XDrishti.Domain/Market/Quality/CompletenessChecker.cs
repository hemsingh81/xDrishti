using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Domain.Market.Quality;

/// <summary>Completeness: for every session of the window after the day of the instrument's first bar, the minutes the calendar expects against the minutes stored.</summary>
public static class CompletenessChecker
{
    /// <summary>The most missing ranges that are kept (the totals are exact regardless).</summary>
    public const int MaxRanges = 500;

    public static (CompletenessCheck Check, IReadOnlyList<MissingRange> Missing) Check(
        TradingCalendar calendar,
        IReadOnlyList<DateOnly> sessions,
        DateTimeOffset? firstBar,
        IReadOnlyList<Bar> bars,
        IReadOnlySet<DateOnly> unfillable,
        double maxMissingPercent)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(bars);
        ArgumentNullException.ThrowIfNull(unfillable);

        // The first day of an instrument is usually part of a session (a listing, a first file): it is not held against it.
        var firstDay = firstBar is { } first ? IndiaTime.Today(first) : DateOnly.MinValue;
        var present = new Dictionary<DateOnly, bool[]>();
        foreach (var date in sessions)
        {
            if (date > firstDay && calendar.SessionOf(date) is { } session)
            {
                present[date] = new bool[session.Minutes];
            }
        }

        foreach (var bar in bars)
        {
            if (present.TryGetValue(SessionClock.DateOf(bar.EpochSeconds), out var minutes))
            {
                var index = SessionClock.MinuteOfDay(bar.EpochSeconds) - calendar.SessionOf(SessionClock.DateOf(bar.EpochSeconds))!.Value.FromMinute;
                if (index >= 0 && index < minutes.Length)
                {
                    minutes[index] = true;
                }
            }
        }

        var perSession = new List<SessionCompleteness>();
        var ranges = new List<MissingRange>();
        int expectedTotal = 0, foundTotal = 0, unfillableMinutes = 0;
        foreach (var date in sessions.Where(present.ContainsKey))
        {
            var minutes = present[date];
            var from = calendar.SessionOf(date)!.Value.FromMinute;
            var found = minutes.Count(m => m);
            var missing = minutes.Length - found;
            var isUnfillable = missing > 0 && unfillable.Contains(date);
            perSession.Add(new SessionCompleteness(date, minutes.Length, found, missing, isUnfillable));
            expectedTotal += minutes.Length;
            foundTotal += found;
            unfillableMinutes += isUnfillable ? missing : 0;
            AddRanges(ranges, date, from, minutes);
        }

        var missingTotal = expectedTotal - foundTotal;
        var percent = expectedTotal == 0 ? 0 : Math.Round(missingTotal * 100.0 / expectedTotal, 2);
        var status = percent > maxMissingPercent || unfillableMinutes > 0 ? CheckStatus.Warning : CheckStatus.Pass;
        return (new CompletenessCheck(status, expectedTotal, foundTotal, missingTotal, percent, unfillableMinutes, perSession), ranges);
    }

    private static void AddRanges(List<MissingRange> ranges, DateOnly date, int sessionFrom, bool[] minutes)
    {
        for (var i = 0; i < minutes.Length && ranges.Count < MaxRanges; i++)
        {
            if (minutes[i])
            {
                continue;
            }

            var start = i;
            while (i + 1 < minutes.Length && !minutes[i + 1])
            {
                i++;
            }

            ranges.Add(new MissingRange(date, sessionFrom + start, i - start + 1));
        }
    }
}
