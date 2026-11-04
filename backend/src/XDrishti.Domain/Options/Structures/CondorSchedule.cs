using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Options.Remark;

namespace XDrishti.Domain.Options.Structures;

/// <summary>
/// The entries of the range family (spec P14-10): on the first session of each trading week, 09:30, when the previous session was labelled <c>range</c>, the nearest weekly expiry that is at least two sessions away; the position is held to the close of the session before it (D6)
/// or its stop. The weekly series are the ones the backfill holds (NIFTY <c>W1</c>, <c>W2</c>; BANKNIFTY <c>W1</c> until its weeklies were withdrawn): an expiry that needs another series is counted (<see cref="NoSeries"/>), never skipped silently. A week whose previous session is not a range
/// session is counted (<see cref="NotRange"/>), and so is a week with no weekly expiry (<see cref="NoWeekly"/>). Across the 2025-09-01 change to Tuesday expiries the first session of the week is one session from the Tuesday, so the entry takes the following week's expiry. Pure.
/// </summary>
public sealed class CondorSchedule
{
    /// <summary>The entry minute, India time: 09:30.</summary>
    public const int EntryMinute = (9 * 60) + 30;

    /// <summary>The fewest sessions between the entry and the expiry.</summary>
    public const int MinSessionsAway = 2;

    private CondorSchedule(IReadOnlyList<CondorEntry> entries, int weeks, int notRange, int noWeekly, int noSeries)
    {
        Entries = entries;
        Weeks = weeks;
        NotRange = notRange;
        NoWeekly = noWeekly;
        NoSeries = noSeries;
    }

    public IReadOnlyList<CondorEntry> Entries { get; }

    /// <summary>The trading weeks that start inside the range.</summary>
    public int Weeks { get; }

    /// <summary>Weeks whose previous session was not labelled <c>range</c> (or has no label).</summary>
    public int NotRange { get; }

    /// <summary>Weeks with no weekly expiry the rule can give.</summary>
    public int NoWeekly { get; }

    /// <summary>Weeks whose expiry needs a series the backfill does not hold.</summary>
    public int NoSeries { get; }

    /// <summary>The last trading day before the date; null when none is found within two weeks.</summary>
    public static DateOnly? PreviousSession(TradingCalendar calendar, DateOnly date)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        for (var back = 1; back <= 14; back++)
        {
            if (calendar.IsTradingDay(date.AddDays(-back)))
            {
                return date.AddDays(-back);
            }
        }

        return null;
    }

    public static CondorSchedule Build(string canonicalSymbol, DateOnly from, DateOnly to, TradingCalendar calendar, ExpiryCalendar expiries, IEnumerable<MarketEpoch> epochs, Func<DateOnly, bool> previousSessionIsRange)
    {
        ArgumentNullException.ThrowIfNull(canonicalSymbol);
        ArgumentNullException.ThrowIfNull(calendar);
        ArgumentNullException.ThrowIfNull(expiries);
        ArgumentNullException.ThrowIfNull(epochs);
        ArgumentNullException.ThrowIfNull(previousSessionIsRange);
        var weeklyEnd = ExpiryRules.BankNiftyWeeklyEnd(epochs);
        List<CondorEntry> entries = [];
        int weeks = 0, notRange = 0, noWeekly = 0, noSeries = 0;
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            if (!calendar.IsTradingDay(date) || PreviousSession(calendar, date) is not { } previous || !IsFirstOfWeek(date, previous))
            {
                continue;
            }

            weeks++;
            if (!previousSessionIsRange(previous))
            {
                notRange++;
                continue;
            }

            var weeklyExists = canonicalSymbol == ExpiryRules.Nifty || (canonicalSymbol == ExpiryRules.BankNifty && date <= weeklyEnd);
            var chosen = weeklyExists ? Choose(canonicalSymbol, date, calendar, expiries, weeklyEnd) : null;
            switch (chosen)
            {
                case null when !weeklyExists:
                    noWeekly++;
                    break;
                case null:
                    noSeries++;
                    break;
                default:
                    entries.Add(new CondorEntry(date, chosen));
                    break;
            }
        }

        return new CondorSchedule(entries, weeks, notRange, noWeekly, noSeries);
    }

    /// <summary>The entry instant in epoch seconds: 09:30 India time on the date.</summary>
    public static long EntryEpoch(DateOnly date) => new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), IndiaTime.Offset).AddMinutes(EntryMinute).ToUnixTimeSeconds();

    private static bool IsFirstOfWeek(DateOnly date, DateOnly previous) => WeekStart(date) != WeekStart(previous);

    private static DateOnly WeekStart(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    private static SeriesPlan? Choose(string symbol, DateOnly date, TradingCalendar calendar, ExpiryCalendar expiries, DateOnly weeklyEnd)
    {
        for (var code = 1; code <= 2; code++)
        {
            if (expiries.Nth('W', date, code) is not { } expiry)
            {
                return null;
            }

            if (SessionsAway(calendar, date, expiry.Date) < MinSessionsAway)
            {
                continue;
            }

            return SeriesPlanner.Held(symbol, 'W', code, date, weeklyEnd) && expiries.LastSessionBefore(expiry.Date) is { } last
                ? new SeriesPlan('W', code, expiry, SeriesPlanner.IndexInterval, last)
                : null;
        }

        return null;
    }

    private static int SessionsAway(TradingCalendar calendar, DateOnly from, DateOnly expiry)
    {
        var sessions = 0;
        for (var date = from.AddDays(1); date <= expiry; date = date.AddDays(1))
        {
            if (calendar.IsTradingDay(date))
            {
                sessions++;
            }
        }

        return sessions;
    }
}
