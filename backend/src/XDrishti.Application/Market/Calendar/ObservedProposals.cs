using System.Globalization;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Application.Market.Calendar;

/// <summary>A day the stored bars suggest is not an ordinary session: a suspected holiday, or a special session with the window its bars lie in.</summary>
internal sealed record ObservedProposal(DateOnly Date, CalendarDayKind Kind, int? FromMinute, int? ToMinute, string Note);

/// <summary>
/// The observed rule of ADR 0017 §1, from the daily aggregates of the stored bars (pure):
/// <list type="bullet">
/// <item>A <b>weekday</b> on which fewer than 5 % of the instruments that have bars before and after it have any bar on it is a suspected <b>holiday</b>. An ordinary day, on which
/// every instrument that trades has a bar, is nowhere near; so is one on which a handful of instruments are missing. A day on which almost nobody has a bar is suspected, however many
/// stray bars it has — it is only a proposal, the owner decides.</item>
/// <item>A <b>weekend</b> date with bars, or a weekday whose bars lie in the evening window (a Muhurat session), is a <b>special</b> session, its window the one most instruments share.
/// It needs bars from at least 5 % of the instruments (at least one).</item>
/// <item>A <b>weekday that is a confirmed holiday</b> (NSE's list, say) on which at least 5 % of the instruments have bars is a <b>special</b> session too, whatever its window: a
/// holiday that was moved or called off trades a regular session, a Muhurat session is in the evening. It is only a proposal next to the holiday, which stays in force until the
/// owner decides.</item>
/// </list>
/// Dates are the India dates of the daily rows. Instruments with bars on only one side of a date say nothing about it, so a date before the first or after the last bar is never a holiday
/// (it is "behind", not closed).
/// </summary>
internal static class ObservedProposals
{
    /// <summary>Below this share of the instruments around it a weekday is suspected a holiday, and below it a date has too few bars to be a special session.</summary>
    public const int Percent = 5;

    /// <summary>The most dates whose bars are looked at to find their window (the newest first).</summary>
    public const int MaxWindowDates = 200;

    private static readonly HashSet<DateOnly> NoDates = [];

    /// <summary>
    /// The dates whose window must be read to decide whether they are special sessions: weekend dates with enough bars, weekdays that do not have complete sessions, and
    /// confirmed holidays with enough bars.
    /// </summary>
    public static IReadOnlyList<DateOnly> WindowCandidates(DayPresence presence, IReadOnlySet<DateOnly>? confirmedHolidays = null)
    {
        ArgumentNullException.ThrowIfNull(presence);
        confirmedHolidays ??= NoDates;
        var quorum = Quorum(presence.Spans.Count);
        return [.. presence.Days
            .Where(d => d.Instruments >= quorum && (IsWeekend(d.Date) || d.MedianBars < SessionClock.RegularBars || confirmedHolidays.Contains(d.Date)))
            .OrderByDescending(d => d.Date)
            .Take(MaxWindowDates)
            .Select(d => d.Date)];
    }

    public static IReadOnlyList<ObservedProposal> Build(DayPresence presence, IReadOnlyDictionary<DateOnly, ObservedWindow> windows, IReadOnlySet<DateOnly>? confirmedHolidays = null)
    {
        ArgumentNullException.ThrowIfNull(presence);
        ArgumentNullException.ThrowIfNull(windows);
        confirmedHolidays ??= NoDates;
        if (presence.Days.Count == 0)
        {
            return [];
        }

        var proposals = new SortedDictionary<DateOnly, ObservedProposal>();
        AddSpecials(presence, windows, confirmedHolidays, proposals);
        AddHolidays(presence, proposals);
        return [.. proposals.Values];
    }

    private static void AddSpecials(DayPresence presence, IReadOnlyDictionary<DateOnly, ObservedWindow> windows, IReadOnlySet<DateOnly> confirmedHolidays, SortedDictionary<DateOnly, ObservedProposal> proposals)
    {
        var quorum = Quorum(presence.Spans.Count);
        foreach (var day in presence.Days)
        {
            if (day.Instruments < quorum || !windows.TryGetValue(day.Date, out var window) || window.Instruments < quorum)
            {
                continue;
            }

            var special = IsWeekend(day.Date) || window.FromMinute >= SessionClock.EveningSessionFromMinute || confirmedHolidays.Contains(day.Date);
            if (special && TradingDay.IsValidWindow(window.FromMinute, window.ToMinute))
            {
                proposals[day.Date] = new ObservedProposal(
                    day.Date,
                    CalendarDayKind.Special,
                    window.FromMinute,
                    window.ToMinute,
                    string.Create(CultureInfo.InvariantCulture, $"Seen in the stored bars: {window.Instruments} of {presence.Spans.Count} instruments have bars from {Clock(window.FromMinute)} to {Clock(window.ToMinute)}."));
            }
        }
    }

    private static void AddHolidays(DayPresence presence, SortedDictionary<DateOnly, ObservedProposal> proposals)
    {
        var first = presence.Days.Min(d => d.Date);
        var last = presence.Days.Max(d => d.Date);
        var length = last.DayNumber - first.DayNumber + 1;

        // references[i] = the instruments that have bars on both sides of the i-th date after the first: first < date < last.
        var delta = new int[length + 1];
        foreach (var span in presence.Spans)
        {
            var from = Math.Max(span.First.DayNumber, first.DayNumber) - first.DayNumber + 1;
            var to = Math.Min(span.Last.DayNumber, last.DayNumber) - first.DayNumber;
            if (from < to)
            {
                delta[from]++;
                delta[to]--;
            }
        }

        var byDate = presence.Days.ToDictionary(d => d.Date);
        var references = 0;
        for (var i = 0; i < length; i++)
        {
            references += delta[i];
            var date = first.AddDays(i);
            if (references == 0 || IsWeekend(date) || proposals.ContainsKey(date))
            {
                continue;
            }

            var inside = byDate.TryGetValue(date, out var day) ? day.Inside : 0;
            if (inside * 100 < references * Percent)
            {
                proposals[date] = new ObservedProposal(
                    date,
                    CalendarDayKind.Holiday,
                    null,
                    null,
                    string.Create(CultureInfo.InvariantCulture, $"Seen in the stored bars: {inside} of {references} instruments with bars before and after this date have bars on it."));
            }
        }
    }

    private static int Quorum(int instruments) => Math.Max(1, (instruments * Percent + 99) / 100);

    private static bool IsWeekend(DateOnly date) => date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    private static string Clock(int minute) => string.Create(CultureInfo.InvariantCulture, $"{minute / 60:00}:{minute % 60:00}");
}
