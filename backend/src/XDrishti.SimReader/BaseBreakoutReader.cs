using System.Globalization;

namespace XDrishti.SimReader;

/// <summary>
/// Reads one BASE_BREAKOUT trade from the daily bars built off 1-minute bars, from the rule tables of the strategy spec (P3-06 D1 to D4) and ADR 0037, 0038, 0040 (X8), 0043 (E8, E9) only.
/// The decision is taken once, at the close of the signal session; the order rests overnight and fills in the first five minutes of the next session; the position leaves at the close of the last minute of the fifth session of the hold.
/// Written without any engine, strategy or indicator code. Pure, decimal, no clock, no calendar (a session is a date that has bars; the last session of a week is the one followed by a session of a later week).
/// Returns null when the rules give no trade.
/// </summary>
public static class BaseBreakoutReader
{
    private const int WeeklyAverage = 10;
    private const int DailyAverage = 50;
    private const int VolumeSessions = 20;
    private const int MaxFillDelayMinutes = 5;

    /// <param name="sessions">The sessions in ascending date order: the history, the signal session, and the sessions after it (for the fill and the hold).</param>
    /// <param name="signalDate">The decision date (the date of the trade key).</param>
    /// <param name="strategy">The recorded definition.</param>
    public static ReaderReading? Read(IReadOnlyList<ReaderSession> sessions, DateOnly signalDate, BaseBreakoutStrategy strategy)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(strategy);
        if (strategy.BaseLen < 1 || strategy.BasePct <= 0m || strategy.VolX <= 0m || strategy.RewardRisk <= 0m || strategy.Tick <= 0m || strategy.MaxHoldSessions < 1 || strategy.EntryLimitPercent < 0m)
        {
            throw new ArgumentException("base_len, base_pct, vol_x, reward-risk, tick and the hold must be above 0 and the limit percent not negative.", nameof(strategy));
        }

        var dailies = sessions.Where(s => s.Bars.Count > 0).Select(ReaderDaily.Of).Where(d => d is not null).Select(d => d!).OrderBy(d => d.Date).ToList();
        var at = dailies.FindIndex(d => d.Date == signalDate);
        if (at < 0 || at + 1 >= dailies.Count)
        {
            return null; // no signal session, or no next session for the resting order
        }

        var signal = dailies[at];
        if (ReaderClock.MinuteOfDay(new ReaderBar(signal.LastBarEpoch, 0m, 0m, 0m, 0m, 0m)) != ReaderClock.SessionClose - 1)
        {
            return null; // the 15:29 minute is missing: the daily bar closes late and is stale, the signal is lost (D5)
        }

        var history = dailies.Take(at + 1).ToList();
        var closes = history.Select(d => d.Close).ToList();
        var window = closes.Skip(Math.Max(0, closes.Count - strategy.BaseLen)).ToList();
        var volumes = history.Select(d => d.Volume).ToList();
        if (closes.Count < DailyAverage || window.Count < strategy.BaseLen || volumes.Count < VolumeSessions + 1)
        {
            return null; // the averages are not ready
        }

        var lastOfWeek = WeekOf(dailies[at + 1].Date) != WeekOf(signal.Date);
        if (!WeeklyTrend(history, lastOfWeek) || !(signal.Close > ReaderMath.Sma(closes, DailyAverage)))
        {
            return null;
        }

        var low = window.Min();
        var baseHigh = window.Max();
        var mean = volumes.Skip(volumes.Count - 1 - VolumeSessions).Take(VolumeSessions).Average();
        if (low <= 0m || (baseHigh - low) / low * 100m > strategy.BasePct || mean <= 0m || signal.Volume < strategy.VolX * mean || signal.Close < baseHigh)
        {
            return null;
        }

        var stop = history.Skip(history.Count - strategy.BaseLen).Min(d => d.Low);
        var fillSession = ReaderClock.SessionBars(sessions.First(s => s.Date == dailies[at + 1].Date).Bars);
        var firstTrade = fillSession.FindIndex(b => b.Volume > 0m);
        if (firstTrade < 0 || ReaderClock.MinuteOfDay(fillSession[firstTrade]) - ReaderClock.SessionOpen > MaxFillDelayMinutes)
        {
            return null; // no minute with a trade in the first five minutes
        }

        if (ReaderWalk.LimitFill(fillSession[firstTrade], true, signal.Close, strategy.Tick, strategy.EntryLimitPercent, stop) is not { } fill)
        {
            return null; // limit not reached, or the open is at or through the stop
        }

        // the walk: the minutes of the fill session and the sessions after it
        var dueAt = at + strategy.MaxHoldSessions;
        var walkDays = dailies.Skip(at + 1).Take(dueAt < dailies.Count ? strategy.MaxHoldSessions : dailies.Count).Select(d => d.Date).ToHashSet();
        var bars = new List<ReaderBar>();
        foreach (var session in sessions.Where(s => walkDays.Contains(s.Date)).OrderBy(s => s.Date))
        {
            bars.AddRange(ReaderClock.SessionBars(session.Bars));
        }

        var fillIndex = bars.FindIndex(b => b.EpochSeconds == fillSession[firstTrade].EpochSeconds);
        var risk = fill.Price - stop;
        var target = fill.Price + (strategy.RewardRisk * risk);
        long? dueDay = dueAt < dailies.Count ? ReaderClock.DayOf(new ReaderBar(dailies[dueAt].LastBarEpoch, 0m, 0m, 0m, 0m, 0m)) : null;
        var (exitIndex, exitPrice, reason) = ReaderWalk.Exit(bars, true, fillIndex, fill.AtLimit, stop, target, null, dueDay);
        return new ReaderReading(
            0m, 0m, signal.LastBarEpoch, signal.Close, bars[fillIndex].EpochSeconds, fill.Price, fill.AtLimit, stop, target, bars[exitIndex].EpochSeconds, exitPrice, reason, Math.Round((exitPrice - fill.Price) / risk, 6));
    }

    private static (int Year, int Week) WeekOf(DateOnly date) => (ISOWeek.GetYear(date.ToDateTime(TimeOnly.MinValue)), ISOWeek.GetWeekOfYear(date.ToDateTime(TimeOnly.MinValue)));

    /// <summary>The close of the last closed week against the mean of the last 10 weekly closes; the week of the signal is closed only when the signal session is the last of its week.</summary>
    private static bool WeeklyTrend(List<ReaderDaily> history, bool signalWeekClosed)
    {
        var weeks = history.GroupBy(d => WeekOf(d.Date)).Select(g => g.Last().Close).ToList();
        if (!signalWeekClosed)
        {
            weeks.RemoveAt(weeks.Count - 1);
        }

        return ReaderMath.Sma(weeks, WeeklyAverage) is { } average && weeks[^1] > average;
    }
}
