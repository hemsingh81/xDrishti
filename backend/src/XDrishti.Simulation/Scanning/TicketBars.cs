using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Simulation.Scanning;

/// <summary>
/// The bars of one instrument prepared once for every cell (spec P7-04, ADR 0088): bars at or after the close of D dropped, sorted, the completeness of D checked, the 20-session turnover and the daily bars (one bar per day,
/// with the open, high, low, close and volume of the day, at the day's last minute) that a side reading only 1D or longer data is stepped with instead of every minute. Pure.
/// </summary>
public sealed class TicketBars
{
    private const long IndiaOffsetSeconds = 19800;

    private TicketBars(IReadOnlyList<Bar> minutes, IReadOnlyList<Bar> daily, bool complete, double? turnover, DateOnly session)
    {
        Minutes = minutes;
        Daily = daily;
        IsComplete = complete;
        Turnover = turnover;
        Session = session;
    }

    /// <summary>The 1-minute bars before the close of D, ascending.</summary>
    public IReadOnlyList<Bar> Minutes { get; }

    /// <summary>One bar per session, built from <see cref="Minutes"/>.</summary>
    public IReadOnlyList<Bar> Daily { get; }

    /// <summary>Whether the last minute of D is present (the session is complete in the bars).</summary>
    public bool IsComplete { get; }

    /// <summary>Mean of close times volume over the last 20 sessions, or null when there are fewer.</summary>
    public double? Turnover { get; }

    /// <summary>The session D.</summary>
    public DateOnly Session { get; }

    /// <summary>
    /// Prepares the bars. The close of D is the end of the calendar's session of D when it has one (a short or special session), otherwise 15:30; every bar at or after it is dropped, so nothing after the close can reach a ticket.
    /// </summary>
    public static TicketBars Prepare(IEnumerable<Bar> bars, DateOnly session, TradingCalendar? calendar)
    {
        ArgumentNullException.ThrowIfNull(bars);
        var closeMinute = calendar?.SessionOf(session) is { } s ? s.ToMinute : SessionClock.CloseMinute;
        var cut = new DateTimeOffset(session.ToDateTime(TimeOnly.MinValue), TimeSpan.FromSeconds(IndiaOffsetSeconds)).AddMinutes(closeMinute).ToUnixTimeSeconds();
        var kept = bars.Where(b => b.EpochSeconds < cut).OrderBy(b => b.EpochSeconds).ToList();
        var complete = kept.Count > 0 && kept[^1].EpochSeconds == cut - 60;
        var days = kept.GroupBy(b => SessionClock.DateOf(b.EpochSeconds)).OrderBy(g => g.Key).ToList();
        var daily = new List<Bar>(days.Count);
        foreach (var day in days)
        {
            var list = day.ToList();
            daily.Add(new Bar(list[^1].EpochSeconds, list[0].Open, list.Max(b => b.High), list.Min(b => b.Low), list[^1].Close, list.Sum(b => b.Volume)));
        }

        double? turnover = days.Count < TicketBuilder.TurnoverSessions ? null : days.TakeLast(TicketBuilder.TurnoverSessions).Average(g => g.Sum(b => b.Close * b.Volume));
        return new TicketBars(kept, daily, complete, turnover, session);
    }
}
