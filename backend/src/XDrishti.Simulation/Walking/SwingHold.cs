using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Research;

namespace XDrishti.Simulation.Walking;

/// <summary>
/// How long a swing position may be held, counted in sessions of the trading calendar (spec P3-03d, ADR 0040, D3): the entry session is session 1, so a hold of N sessions leaves at the close of the Nth session counting the entry
/// day. With a calendar a session is a date for which <see cref="TradingCalendar.IsTradingDay"/> is true (a special session included); without one every weekday is a session (the default week), so a holiday is counted in N and the
/// walk sees it as a session without bars. A walk with a <see cref="SwingHold"/> is a swing walk (X7 to X9): no square-off, no end at midnight.
/// </summary>
public sealed record SwingHold
{
    /// <summary>The longest hold, in sessions: 1 to 5 (doc 08 §5).</summary>
    public const int MaxSessions = 5;

    /// <summary>How far ahead the search for the next trading day looks: a calendar full of holidays must not loop for ever.</summary>
    internal const int SearchDays = 30;

    private static readonly TradingCalendar DefaultWeek = new();

    private static readonly TimeSpan IndiaOffset = TimeSpan.FromMinutes(330);

    private SwingHold(int maxHoldSessions, TradingCalendar? calendar)
    {
        MaxHoldSessions = maxHoldSessions;
        Calendar = calendar;
    }

    /// <summary>The longest hold in sessions, counting the entry session as the first.</summary>
    public int MaxHoldSessions { get; }

    /// <summary>The trading calendar that counts the sessions; null for the default week (every weekday is a session).</summary>
    public TradingCalendar? Calendar { get; }

    /// <summary>A hold of <paramref name="maxHoldSessions"/> sessions (1 to 5) counted with <paramref name="calendar"/> (null: the default week); <c>simulation.rules_invalid</c> for any other number.</summary>
    public static Result<SwingHold> Create(int maxHoldSessions, TradingCalendar? calendar) =>
        maxHoldSessions is >= 1 and <= MaxSessions
            ? Result.Success(new SwingHold(maxHoldSessions, calendar))
            : Result.Failure<SwingHold>(Error.Validation(SimulationErrors.RulesInvalidCode, "The longest hold must be from 1 to " + MaxSessions + " sessions."));

    /// <summary>The due session of a position entered on <paramref name="entryDate"/>: N − 1 trading days after it. No trading day within 30 days of a step is <c>simulation.rules_invalid</c>.</summary>
    internal Result<DateOnly> DueSession(DateOnly entryDate) => DueSession(entryDate, MaxHoldSessions);

    /// <summary>The session that is number <paramref name="sessions"/> counting the entry session as the first: the due session of a hold of that many sessions (also the checked session of the swing time stop, P4-02, D9).</summary>
    internal Result<DateOnly> DueSession(DateOnly entryDate, int sessions)
    {
        var calendar = Calendar ?? DefaultWeek;
        var date = entryDate;
        for (var step = 1; step < sessions; step++)
        {
            var found = false;
            for (var ahead = 1; ahead <= SearchDays && !found; ahead++)
            {
                var candidate = date.AddDays(ahead);
                if (calendar.IsTradingDay(candidate))
                {
                    date = candidate;
                    found = true;
                }
            }

            if (!found)
            {
                return Result.Failure<DateOnly>(Error.Validation(SimulationErrors.RulesInvalidCode, "No trading day was found within " + SearchDays + " days after " + date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) + " in the calendar."));
            }
        }

        return Result.Success(date);
    }

    /// <summary>The due minute of the session: the one that starts one minute before its end (15:29 for a regular session; a special session has its own end).</summary>
    internal int DueMinute(DateOnly session) => (Calendar ?? DefaultWeek).SessionOf(session)?.ToMinute - 1 ?? SessionClock.CloseMinute - 1;

    /// <summary>The minute of the day at which the session ends (<c>ToMinute</c>: 15:30 for a regular session and for a date the calendar does not know).</summary>
    internal int SessionEndMinute(DateOnly session) => (Calendar ?? DefaultWeek).SessionOf(session)?.ToMinute ?? SessionClock.CloseMinute;

    /// <summary>The instant the session opens (<c>FromMinute</c>, 09:15 for a regular session): the origin of the five minutes in which an order resting overnight may fill (E9).</summary>
    internal long SessionOpen(DateOnly session) => EpochOf(session) + (((Calendar ?? DefaultWeek).SessionOf(session)?.FromMinute ?? SessionClock.OpenMinute) * 60L);

    /// <summary>The fill session of an order placed on <paramref name="decisionDate"/> that rests overnight (E9): the next trading day after it by the calendar (the next weekday without one). No trading day within 30 days is <c>simulation.rules_invalid</c>.</summary>
    internal Result<DateOnly> NextSession(DateOnly decisionDate)
    {
        var calendar = Calendar ?? DefaultWeek;
        for (var ahead = 1; ahead <= SearchDays; ahead++)
        {
            var candidate = decisionDate.AddDays(ahead);
            if (calendar.IsTradingDay(candidate))
            {
                return Result.Success(candidate);
            }
        }

        return Result.Failure<DateOnly>(Error.Validation(SimulationErrors.RulesInvalidCode, "No trading day was found within " + SearchDays + " days after " + decisionDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) + " in the calendar."));
    }

    private static long EpochOf(DateOnly date) => new DateTimeOffset(date.Year, date.Month, date.Day, 0, 0, 0, IndiaOffset).ToUnixTimeSeconds();

    /// <summary>The instant the session ends (15:30 India time for a regular session): the hold end of a position that was due in it but whose due minute held no trade (D11).</summary>
    internal long SessionEnd(DateOnly session) => EpochOf(session) + ((DueMinute(session) + 1) * 60L);

    /// <summary><c>1 + trading days after the entry date up to the exit date</c>: the sessions from the entry session to the session of the exit, both included.</summary>
    internal int SessionsBetween(DateOnly entryDate, DateOnly exitDate) => 1 + (Calendar ?? DefaultWeek).TradingDaysBetween(entryDate, exitDate);
}
