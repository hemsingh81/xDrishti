namespace XDrishti.Simulation.Tests.Support;

/// <summary>
/// A plain loop over the daily rows of a <see cref="BaseSeries"/> (spec P3-06, D10 and criterion 11), written from the text of <c>BASE_BREAKOUT</c> and of the swing conventions of ADR 0040 and 0043 with <c>decimal</c> arithmetic and <b>no</b> use of the frame,
/// the language, the compiler, the scanner or the walker: it is the check of them, so it shares no code with them (a test reads this file's text for the names of the internals). It knows the sparse days of the generator: a day whose target and stop are both
/// inside its range is a target day (the minute of the high comes before the minute of the low), a gap through the stop exits at the open, a target is never improved on a gap, a stop is filled at the stop. The series it is given have no missing minute.
/// </summary>
internal static class BaseBreakoutReference
{
    private const int HoldSessions = 5;

    private const int WeeklyAverage = 10;

    private const int DailyAverage = 50;

    private const int VolumeMean = 20;

    private const decimal Limit = 1.001m;

    /// <summary>A decision: the day, the close that triggered, the lowest low of the last <c>base_len</c> sessions.</summary>
    public sealed record Decision(DateOnly Date, decimal TriggerClose, decimal Stop);

    /// <summary>What became of a decision. <see cref="Kind"/> is <c>Trade</c> or the name of the walker's no-trade reason; the other fields are those of a trade.</summary>
    public sealed record Fate(Decision Decision, string Kind, DateOnly FillDate, decimal Entry, DateOnly ExitDate, decimal Exit, string Reason, double RExact);

    /// <summary>The decisions and their fates, in order.</summary>
    public static List<Fate> Run(BaseSeries series)
    {
        var rows = series.Rows;
        var dates = series.Dates;
        var count = rows.Count;
        var fates = new List<Fate>();
        var weekEnds = Enumerable.Range(0, count).Select(i => EndsItsWeek(series, i)).ToArray();
        var closedWeeks = new List<decimal>();
        DateOnly? busyUntil = null;
        var endedWithTheData = false;

        for (var i = 0; i < count; i++)
        {
            List<decimal> weeks = weekEnds[i] ? [.. closedWeeks, rows[i].C] : closedWeeks;
            if (weekEnds[i])
            {
                closedWeeks.Add(rows[i].C);
            }

            if (!Signal(series, i, weeks, out var stop))
            {
                continue;
            }

            var decision = new Decision(dates[i], rows[i].C, stop);
            if (endedWithTheData || (busyUntil is { } until && dates[i] < until))
            {
                fates.Add(NoTrade(decision, "PositionOpen"));
                continue;
            }

            if (i == count - 1)
            {
                fates.Add(NoTrade(decision, "OrderPendingAtEnd"));
                continue;
            }

            var fill = rows[i + 1].O;
            var limit = Math.Floor(rows[i].C * Limit / SeededBase.Tick) * SeededBase.Tick;
            if (fill <= stop)
            {
                fates.Add(NoTrade(decision, "EntryThroughStop"));
                continue;
            }

            if (fill > limit)
            {
                fates.Add(NoTrade(decision, "LimitNotReached"));
                continue;
            }

            var fate = Walk(series, decision, i + 1);
            fates.Add(fate);
            busyUntil = fate.ExitDate;
            endedWithTheData = fate.Reason == "EndOfData";
        }

        return fates;
    }

    private static Fate NoTrade(Decision decision, string kind) => new(decision, kind, default, 0m, default, 0m, string.Empty, 0);

    private static bool Signal(BaseSeries series, int i, List<decimal> weeks, out decimal stop)
    {
        var rows = series.Rows;
        var length = series.BaseLen;
        stop = 0m;
        if (i + 1 < DailyAverage || i < VolumeMean || i + 1 < length || weeks.Count < WeeklyAverage)
        {
            return false;
        }

        var close = rows[i].C;
        var weeklyTrend = weeks[^1] > weeks.TakeLast(WeeklyAverage).Average();
        var dailyTrend = close > rows.Skip(i + 1 - DailyAverage).Take(DailyAverage).Average(r => r.C);
        var window = rows.Skip(i + 1 - length).Take(length).ToList();
        var top = window.Max(r => r.C);
        var bottom = window.Min(r => r.C);
        var tight = (top - bottom) / bottom * 100 <= series.BasePct;
        var mean = rows.Skip(i - VolumeMean).Take(VolumeMean).Average(r => (decimal)r.V);
        var volume = rows[i].V >= series.VolX * mean;
        var breakout = close >= top;
        stop = window.Min(r => r.L);
        return weeklyTrend && dailyTrend && tight && volume && breakout;
    }

    private static Fate Walk(BaseSeries series, Decision decision, int fillIndex)
    {
        var rows = series.Rows;
        var dates = series.Dates;
        var entry = rows[fillIndex].O;
        var risk = entry - decision.Stop;
        var target = entry + (series.RewardRisk * risk);
        for (var j = fillIndex; j < rows.Count; j++)
        {
            var row = rows[j];
            var price = 0m;
            var reason = string.Empty;
            if (j > fillIndex && row.O <= decision.Stop)
            {
                (price, reason) = (row.O, "Stop");
            }
            else if (row.H >= target)
            {
                (price, reason) = (target, "Target");
            }
            else if (row.L <= decision.Stop)
            {
                (price, reason) = (decision.Stop, "Stop");
            }
            else if (j - fillIndex + 1 == HoldSessions)
            {
                (price, reason) = (row.C, "MaxHold");
            }
            else if (j == rows.Count - 1)
            {
                (price, reason) = (row.C, "EndOfData");
            }

            if (reason.Length > 0)
            {
                return new Fate(decision, "Trade", dates[fillIndex], entry, dates[j], price, reason, (double)((price - entry) / risk));
            }
        }

        throw new InvalidOperationException("The walk ended without an exit.");
    }

    /// <summary>True when the session is the last of its calendar week: the next trading day (the next session of the series, or after the last one the next weekday that is not a holiday) is in a later week.</summary>
    private static bool EndsItsWeek(BaseSeries series, int i)
    {
        var next = i + 1 < series.Dates.Count ? series.Dates[i + 1] : NextTradingDay(series, series.Dates[i]);
        return Monday(next) != Monday(series.Dates[i]);
    }

    private static DateOnly NextTradingDay(BaseSeries series, DateOnly date)
    {
        var next = date.AddDays(1);
        while (next.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday || series.Holidays.Contains(next))
        {
            next = next.AddDays(1);
        }

        return next;
    }

    private static DateOnly Monday(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
}
