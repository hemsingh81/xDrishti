using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Market.Quality;

namespace XDrishti.Domain.Reporting;

/// <summary>
/// The bars a trade replay shows (P8-11): from the open of the session <c>before</c> sessions before the entry session to the close of the session <c>after</c> sessions after the exit session, counted on the trading calendar (weekends and holidays are
/// skipped, never drawn). Pure. The timeframes a replay may use are 1m, 5m, 15m, 1h and 1D; intraday ones take 0 to 3 sessions on each side, the day one 0 to 60.
/// </summary>
public static class TradeReplayWindow
{
    /// <summary>The most bars one replay returns; a window with more is refused, never thinned.</summary>
    public const int MaxBars = 5_000;

    private static readonly Timeframe[] Allowed = [Timeframe.M1, Timeframe.M5, Timeframe.M15, Timeframe.H1, Timeframe.D1];

    /// <summary>The timeframes in the order a screen lists them.</summary>
    public static IReadOnlyList<Timeframe> Timeframes => Allowed;

    /// <summary>True for 1m, 5m, 15m, 1h and 1D.</summary>
    public static bool IsAllowed(Timeframe timeframe) => Array.IndexOf(Allowed, timeframe) >= 0;

    /// <summary>The sessions before the entry when none is asked for: one for an intraday timeframe, 20 for the day one.</summary>
    public static int DefaultBefore(Timeframe timeframe) => timeframe == Timeframe.D1 ? 20 : 1;

    /// <summary>The sessions after the exit when none is asked for: none for an intraday timeframe, 20 for the day one.</summary>
    public static int DefaultAfter(Timeframe timeframe) => timeframe == Timeframe.D1 ? 20 : 0;

    /// <summary>The most sessions on each side: 3, or 60 for the day timeframe.</summary>
    public static int MaxSessions(Timeframe timeframe) => timeframe == Timeframe.D1 ? 60 : 3;

    /// <summary>The window of a trade that entered on <paramref name="entryDate"/> and left on <paramref name="exitDate"/>.</summary>
    public static ReplayWindow Compute(TradingCalendar calendar, DateOnly entryDate, DateOnly exitDate, int before, int after)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        ArgumentOutOfRangeException.ThrowIfNegative(before);
        ArgumentOutOfRangeException.ThrowIfNegative(after);
        var from = Shift(calendar, entryDate, -before);
        var to = Shift(calendar, exitDate < entryDate ? entryDate : exitDate, after);
        var open = (calendar.SessionOf(from)?.FromMinute ?? SessionClock.OpenMinute) * 60L;
        var close = (calendar.SessionOf(to)?.ToMinute ?? SessionClock.CloseMinute) * 60L;
        return new ReplayWindow(MinuteAggregator.MidnightOf(from) + open, MinuteAggregator.MidnightOf(to) + close - 60, from, to);
    }

    /// <summary>The trading date <paramref name="sessions"/> sessions from the date (negative: earlier); the walk is bounded so an empty calendar cannot loop.</summary>
    private static DateOnly Shift(TradingCalendar calendar, DateOnly date, int sessions)
    {
        var step = sessions < 0 ? -1 : 1;
        var left = Math.Abs(sessions);
        var current = date;
        for (var guard = 0; left > 0 && guard < (Math.Abs(sessions) * 10) + 30; guard++)
        {
            current = current.AddDays(step);
            if (calendar.IsTradingDay(current))
            {
                left--;
            }
        }

        return current;
    }
}
