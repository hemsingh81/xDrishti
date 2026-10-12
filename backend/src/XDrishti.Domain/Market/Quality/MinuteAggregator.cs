using XDrishti.Domain.Market.Bars;

namespace XDrishti.Domain.Market.Quality;

/// <summary>
/// The data quality check's own implementation of the aggregation rules (design doc 05 §4): builds every derived bar from 1-minute bars in memory with integer calendar arithmetic. It shares
/// nothing with the set-based SQL that builds <c>market.bars_agg</c> (that is what it checks), so a mistake in either shows as a difference. Rules: open is the first bar's open, high the
/// highest, low the lowest, close the last bar's close, volume the sum, <c>Bars</c> the number of 1-minute bars; 5m, 10m, 15m, 30m and 1h buckets start at 09:15 India time plus a whole
/// number of their length on the bar's India date; a day starts at India midnight, a week on Monday, a month on the 1st.
/// </summary>
public static class MinuteAggregator
{
    private const long OffsetSeconds = 330 * 60;

    private const long SecondsPerDay = 86_400;

    /// <summary>Days from 0001-01-01 (<see cref="DateOnly.DayNumber"/>) to 1970-01-01.</summary>
    private const int EpochDayNumber = 719_162;

    private static readonly (Timeframe Timeframe, int Minutes)[] Intraday =
        [(Timeframe.M5, 5), (Timeframe.M10, 10), (Timeframe.M15, 15), (Timeframe.M30, 30), (Timeframe.H1, 60)];

    /// <summary>The recomputed buckets by timeframe and start (epoch seconds, UTC). The bars may come in any order; a minute twice would be counted twice (the table's key forbids it).</summary>
    public static Dictionary<(Timeframe Timeframe, long Start), AggBucket> Build(IReadOnlyList<Bar> bars)
    {
        ArgumentNullException.ThrowIfNull(bars);
        var result = new Dictionary<(Timeframe, long), AggBucket>();
        var ordered = IsAscending(bars) ? bars : [.. bars.OrderBy(b => b.EpochSeconds)];

        // One open bucket per timeframe; because the bars are in time order, a bucket is finished the moment a bar of another one arrives.
        var open = new Running[Intraday.Length + 3];
        foreach (var bar in ordered)
        {
            var local = bar.EpochSeconds + OffsetSeconds;
            var day = FloorDiv(local, SecondsPerDay);
            var minuteOfDay = (int)((local - day * SecondsPerDay) / 60);
            var midnight = day * SecondsPerDay - OffsetSeconds;

            for (var i = 0; i < Intraday.Length; i++)
            {
                var length = Intraday[i].Minutes;
                var bucketMinute = SessionClock.OpenMinute + FloorDiv(minuteOfDay - SessionClock.OpenMinute, length) * length;
                Add(result, ref open[i], Intraday[i].Timeframe, midnight + bucketMinute * 60L, bar);
            }

            Add(result, ref open[Intraday.Length], Timeframe.D1, midnight, bar);
            Add(result, ref open[Intraday.Length + 1], Timeframe.W1, WeekStartDay(day) * SecondsPerDay - OffsetSeconds, bar);
            Add(result, ref open[Intraday.Length + 2], Timeframe.Mo1, MonthStartDay(day) * SecondsPerDay - OffsetSeconds, bar);
        }

        for (var i = 0; i < open.Length; i++)
        {
            if (open[i].Active)
            {
                result[(open[i].Timeframe, open[i].Start)] = open[i].Bucket;
            }
        }

        return result;
    }

    /// <summary>India midnight (epoch seconds) of the date.</summary>
    public static long MidnightOf(DateOnly date) => ((long)date.DayNumber - EpochDayNumber) * SecondsPerDay - OffsetSeconds;

    /// <summary>India midnight (epoch seconds) of the Monday of the week of the date.</summary>
    public static long WeekStartOf(DateOnly date) => WeekStartDay(date.DayNumber - EpochDayNumber) * SecondsPerDay - OffsetSeconds;

    /// <summary>India midnight (epoch seconds) of the first day of the month of the date.</summary>
    public static long MonthStartOf(DateOnly date) => MonthStartDay(date.DayNumber - EpochDayNumber) * SecondsPerDay - OffsetSeconds;

    private static void Add(Dictionary<(Timeframe, long), AggBucket> finished, ref Running current, Timeframe timeframe, long start, in Bar bar)
    {
        if (current.Active && current.Start == start)
        {
            var b = current.Bucket;
            current.Bucket = new AggBucket(b.Open, Math.Max(b.High, bar.High), Math.Min(b.Low, bar.Low), bar.Close, b.Volume + bar.Volume, b.Bars + 1);
            return;
        }

        if (current.Active)
        {
            finished[(current.Timeframe, current.Start)] = current.Bucket;
        }

        current = new Running(true, timeframe, start, new AggBucket(bar.Open, bar.High, bar.Low, bar.Close, bar.Volume, 1));
    }

    private static bool IsAscending(IReadOnlyList<Bar> bars)
    {
        for (var i = 1; i < bars.Count; i++)
        {
            if (bars[i].EpochSeconds < bars[i - 1].EpochSeconds)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The day (days since 1970-01-01) of the Monday of that day's week. 1970-01-01 was a Thursday, three days after a Monday.</summary>
    private static long WeekStartDay(long day) => day - FloorMod(day + 3, 7);

    private static long MonthStartDay(long day)
    {
        var date = DateOnly.FromDayNumber((int)day + EpochDayNumber);
        return new DateOnly(date.Year, date.Month, 1).DayNumber - EpochDayNumber;
    }

    private static long FloorDiv(long a, long b) => a / b - (a % b != 0 && (a < 0) != (b < 0) ? 1 : 0);

    private static long FloorMod(long a, long b) => a - FloorDiv(a, b) * b;

    private struct Running(bool active, Timeframe timeframe, long start, AggBucket bucket)
    {
        public bool Active = active;

        public Timeframe Timeframe = timeframe;

        public long Start = start;

        public AggBucket Bucket = bucket;
    }
}
