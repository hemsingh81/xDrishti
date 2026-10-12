using XDrishti.Domain.Market.Bars;

namespace XDrishti.Api.IntegrationTests.Data;

/// <summary>What a derived bar must be: the reference implementation's answer for one bucket.</summary>
public sealed record ExpectedBar(double Open, double High, double Low, double Close, long Volume, int Bars, bool Complete);

/// <summary>
/// An independent implementation of the aggregation rules (ADR 0016 §6, design 05 §4), written from the rules, not from the SQL: plain loops and calendar arithmetic
/// on India-time <see cref="DateTime"/> values, no database, no shared helpers. Used to check the database's set-based upserts bar for bar.
/// </summary>
public static class ReferenceAggregator
{
    private static readonly TimeSpan Offset = TimeSpan.FromMinutes(330);

    private static readonly DateTime Epoch = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

    public static Dictionary<(string Timeframe, long Start), ExpectedBar> Build(IReadOnlyCollection<Bar> minuteBars)
    {
        var result = new Dictionary<(string, long), ExpectedBar>();
        var ordered = minuteBars.OrderBy(b => b.EpochSeconds).ToList();

        foreach (var (code, minutes) in new[] { ("5m", 5), ("10m", 10), ("15m", 15), ("30m", 30), ("1h", 60) })
        {
            foreach (var group in ordered.GroupBy(b => IntradayStart(Local(b.EpochSeconds), minutes)))
            {
                var startMinute = (int)(group.Key - group.Key.Date).TotalMinutes;
                var expected = startMinute < 15 * 60 + 30 ? Math.Min(minutes, 15 * 60 + 30 - startMinute) : minutes;
                result[(code, ToEpoch(group.Key))] = Combine(group, group.Count() == expected);
            }
        }

        foreach (var day in ordered.GroupBy(b => Local(b.EpochSeconds).Date))
        {
            result[("1D", ToEpoch(day.Key))] = Combine(day, DayIsComplete(day.ToList()));
        }

        foreach (var week in ordered.GroupBy(b => MondayOf(Local(b.EpochSeconds).Date)))
        {
            result[("1W", ToEpoch(week.Key))] = Combine(week, week.GroupBy(b => Local(b.EpochSeconds).Date).All(d => DayIsComplete(d.ToList())));
        }

        foreach (var month in ordered.GroupBy(b => new DateTime(Local(b.EpochSeconds).Year, Local(b.EpochSeconds).Month, 1)))
        {
            result[("1M", ToEpoch(month.Key))] = Combine(month, month.GroupBy(b => Local(b.EpochSeconds).Date).All(d => DayIsComplete(d.ToList())));
        }

        return result;
    }

    private static DateTime Local(long epochSeconds) => Epoch.AddSeconds(epochSeconds).Add(Offset);

    private static long ToEpoch(DateTime local) => (long)(local - Offset - Epoch).TotalSeconds;

    /// <summary>The bucket a minute falls into: windows of N minutes counted from 09:15 of its own day (the minutes before 09:15 count backwards).</summary>
    private static DateTime IntradayStart(DateTime local, int minutes)
    {
        var sinceOpen = (int)(local - local.Date).TotalMinutes - (9 * 60 + 15);
        var window = (int)Math.Floor(sinceOpen / (double)minutes);
        return local.Date.AddMinutes(9 * 60 + 15 + window * minutes);
    }

    private static DateTime MondayOf(DateTime date)
    {
        var d = date;
        while (d.DayOfWeek != DayOfWeek.Monday)
        {
            d = d.AddDays(-1);
        }

        return d;
    }

    /// <summary>All 375 minutes of the regular session are there; or, on a day without any of them (an evening session), no minute between the first and the last bar is missing.</summary>
    private static bool DayIsComplete(List<Bar> day)
    {
        var regular = day.Count(b =>
        {
            var minute = (int)(Local(b.EpochSeconds) - Local(b.EpochSeconds).Date).TotalMinutes;
            return minute >= 9 * 60 + 15 && minute < 15 * 60 + 30;
        });
        var span = (day.Max(b => b.EpochSeconds) - day.Min(b => b.EpochSeconds)) / 60 + 1;
        return regular == 375 || (regular == 0 && day.Count == span);
    }

    private static ExpectedBar Combine(IEnumerable<Bar> bars, bool complete)
    {
        var list = bars.OrderBy(b => b.EpochSeconds).ToList();
        return new ExpectedBar(list[0].Open, list.Max(b => b.High), list.Min(b => b.Low), list[^1].Close, list.Sum(b => b.Volume), list.Count, complete);
    }
}
