using XDrishti.Domain.Market.Bars;

namespace XDrishti.Indicators.Tests.Support;

/// <summary>A bucket as the grouping reference computes it. <see cref="CloseIndex"/> is the index of the 1-minute bar (in the input) whose arrival closes the bucket; <c>int.MaxValue</c> when no bar ever does.</summary>
internal sealed record ReferenceBucket(long Start, double Open, double High, double Low, double Close, long Volume, int Bars, bool Complete, int CloseIndex)
{
    public Bar AsBar => new(Start, Open, High, Low, Close, Volume);
}

/// <summary>
/// The calendar as the weekly and monthly reference sees it, written without <c>TradingCalendar</c>: the special sessions as minute windows by date, and the weekday holidays. A date is a trading day when it has a special
/// session, or is a weekday that is not a holiday.
/// </summary>
internal sealed record ReferenceCalendar(IReadOnlyDictionary<DateOnly, (int From, int To)> Sessions, IReadOnlySet<DateOnly> Holidays)
{
    public bool IsTradingDay(DateOnly date) =>
        Sessions.ContainsKey(date) || (date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && !Holidays.Contains(date));
}

/// <summary>A week or month as the reference computes it, and how many bars of that period it leaves out because the period was over before they came (a date after the calendar's last session of it).</summary>
internal sealed record ReferencePeriod(ReferenceBucket Bucket, int Excluded);

/// <summary>
/// The independent grouping reference for the timeframe builder: the plain <c>GroupBy</c> of the stored-aggregate tests (<c>ReferenceAggregator</c>), written again here on India-time <see cref="DateTime"/> values,
/// plus the step at which a bucket closes: the bar of its last minute (09:15-anchored windows clipped at 15:30; for a day that starts in the regular session, the 15:29 bar), otherwise the next bar of the input.
/// With <c>sessions</c> (the special sessions a trading calendar knows, as minute windows by date) a bucket whose first bar lies before the session end also ends at the session's last minute.
/// </summary>
internal static class ReferenceBuckets
{
    private static readonly TimeSpan Offset = TimeSpan.FromMinutes(330);

    private static readonly DateTime Epoch = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

    public static List<ReferenceBucket> Build(IReadOnlyList<Bar> minuteBars, Timeframe timeframe, IReadOnlyDictionary<DateOnly, (int From, int To)>? sessions = null)
    {
        var indexed = minuteBars.Select((bar, index) => (Bar: bar, Index: index)).ToList();
        var length = timeframe.Minutes();
        var groups = timeframe == Timeframe.D1
            ? indexed.GroupBy(x => Local(x.Bar.EpochSeconds).Date)
            : indexed.GroupBy(x => IntradayStart(Local(x.Bar.EpochSeconds), length));

        var result = new List<ReferenceBucket>();
        foreach (var group in groups.OrderBy(g => g.Key))
        {
            var items = group.OrderBy(x => x.Bar.EpochSeconds).ToList();
            var bars = items.Select(x => x.Bar).ToList();
            var startMinute = (int)(group.Key - group.Key.Date).TotalMinutes;
            bool complete;
            int windowEnd;
            var firstMinute = Minute(items[0].Bar);
            var sessionEnd = sessions is not null && sessions.TryGetValue(DateOnly.FromDateTime(Local(items[0].Bar.EpochSeconds).Date), out var window) && window.To > firstMinute ? window.To : int.MaxValue;
            if (timeframe == Timeframe.D1)
            {
                var regular = bars.Count(b => Minute(b) is >= 9 * 60 + 15 and < 15 * 60 + 30);
                var span = (bars[^1].EpochSeconds - bars[0].EpochSeconds) / 60 + 1;
                complete = regular == 375 || (regular == 0 && bars.Count == span);
                windowEnd = sessionEnd != int.MaxValue ? sessionEnd : Minute(bars[0]) < 15 * 60 + 30 ? 15 * 60 + 30 : int.MaxValue;
            }
            else
            {
                var expected = startMinute < 15 * 60 + 30 ? Math.Min(length, 15 * 60 + 30 - startMinute) : length;
                complete = bars.Count == expected;
                windowEnd = Math.Min(startMinute + (startMinute < 15 * 60 + 30 ? expected : length), sessionEnd);
            }

            var last = items[^1];
            var closeIndex = Minute(last.Bar) + 1 >= windowEnd ? last.Index : last.Index + 1 < minuteBars.Count ? last.Index + 1 : int.MaxValue;
            result.Add(new ReferenceBucket(
                ToEpoch(group.Key),
                bars[0].Open,
                bars.Max(b => b.High),
                bars.Min(b => b.Low),
                bars[^1].Close,
                bars.Sum(b => b.Volume),
                bars.Count,
                complete,
                closeIndex));
        }

        return result;
    }

    /// <summary>
    /// The weekly (Monday to Sunday) or monthly buckets of the bars, as the definitions say: the plain <c>GroupBy</c> of the India dates by the Monday / the 1st, then open = first, high = max, low = min, close = last, volume =
    /// sum, <c>Bars</c> = how many, <c>Complete</c> = every date with bars is complete by the rule of a stored day (all 375 regular minutes, or no regular minute and every minute between its first and last bar).
    /// <para>
    /// <c>CloseIndex</c>: without a calendar, the index of the first bar of a later period (the bar after the last of this one). With a calendar, the index at which the 1D bar of the period's <b>last session</b> closes —
    /// the greatest date of the period that is a trading day — if that date has bars and its day closes (<see cref="Build"/> for 1D); the bars of later dates of the period are then outside the bucket (<c>Excluded</c>).
    /// If that date has no bars or its day never closes, the period closes as without a calendar and holds all its bars.
    /// </para>
    /// </summary>
    public static List<ReferencePeriod> BuildPeriods(IReadOnlyList<Bar> minuteBars, Timeframe timeframe, ReferenceCalendar? calendar = null)
    {
        var indexed = minuteBars.Select((bar, index) => (Bar: bar, Index: index)).ToList();
        var dayClose = calendar is null
            ? null
            : Build(minuteBars, Timeframe.D1, calendar.Sessions).ToDictionary(b => DateOnly.FromDateTime(Local(b.Start).Date), b => b.CloseIndex);

        var result = new List<ReferencePeriod>();
        foreach (var group in indexed.GroupBy(x => PeriodStart(Local(x.Bar.EpochSeconds).Date, timeframe)).OrderBy(g => g.Key))
        {
            var items = group.OrderBy(x => x.Bar.EpochSeconds).ToList();
            var inside = items;
            var closeIndex = items[^1].Index + 1 < minuteBars.Count ? items[^1].Index + 1 : int.MaxValue;
            if (calendar is not null && LastSession(group.Key, timeframe, calendar) is { } last && dayClose!.TryGetValue(last, out var dayCloseIndex) && dayCloseIndex != int.MaxValue)
            {
                closeIndex = dayCloseIndex;
                inside = [.. items.Where(x => DateOnly.FromDateTime(Local(x.Bar.EpochSeconds).Date) <= last)];
            }

            var bars = inside.Select(x => x.Bar).ToList();
            var complete = bars.GroupBy(b => Local(b.EpochSeconds).Date).All(day =>
            {
                var regular = day.Count(b => Minute(b) is >= 9 * 60 + 15 and < 15 * 60 + 30);
                var span = (day.Last().EpochSeconds - day.First().EpochSeconds) / 60 + 1;
                return regular == 375 || (regular == 0 && day.Count() == span);
            });

            result.Add(new ReferencePeriod(
                new ReferenceBucket(ToEpoch(group.Key), bars[0].Open, bars.Max(b => b.High), bars.Min(b => b.Low), bars[^1].Close, bars.Sum(b => b.Volume), bars.Count, complete, closeIndex),
                items.Count - inside.Count));
        }

        return result;
    }

    private static DateTime PeriodStart(DateTime date, Timeframe timeframe) =>
        timeframe == Timeframe.W1 ? date.AddDays(-(((int)date.DayOfWeek + 6) % 7)) : new DateTime(date.Year, date.Month, 1);

    /// <summary>The greatest date of the week or month that starts at <paramref name="start"/> on which the calendar has a session; null when there is none.</summary>
    private static DateOnly? LastSession(DateTime start, Timeframe timeframe, ReferenceCalendar calendar)
    {
        var end = timeframe == Timeframe.W1 ? start.AddDays(7) : start.AddMonths(1);
        for (var date = end.AddDays(-1); date >= start; date = date.AddDays(-1))
        {
            if (calendar.IsTradingDay(DateOnly.FromDateTime(date)))
            {
                return DateOnly.FromDateTime(date);
            }
        }

        return null;
    }

    private static DateTime Local(long epochSeconds) => Epoch.AddSeconds(epochSeconds).Add(Offset);

    private static long ToEpoch(DateTime local) => (long)(local - Offset - Epoch).TotalSeconds;

    private static int Minute(Bar bar) => (int)(Local(bar.EpochSeconds) - Local(bar.EpochSeconds).Date).TotalMinutes;

    private static DateTime IntradayStart(DateTime local, int minutes)
    {
        var sinceOpen = (int)(local - local.Date).TotalMinutes - (9 * 60 + 15);
        var window = (int)Math.Floor(sinceOpen / (double)minutes);
        return local.Date.AddMinutes(9 * 60 + 15 + window * minutes);
    }
}
