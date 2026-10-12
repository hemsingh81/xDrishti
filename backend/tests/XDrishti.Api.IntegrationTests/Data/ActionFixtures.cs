using XDrishti.Domain.Market.Bars;

namespace XDrishti.Api.IntegrationTests.Data;

/// <summary>A day of a generated series: the date and its prices and volume.</summary>
internal sealed record SeriesDay(DateOnly Date, double Open, double High, double Low, double Close, long Volume);

/// <summary>
/// Generated day series for the corporate action tests, loaded through the real CSV pipeline as two 1-minute bars a day (09:15 holds the open, the high, the low and the volume; 15:29 closes), so the
/// 1D aggregate of a day is exactly the day written here. Nothing real is committed.
/// </summary>
internal static class ActionFixtures
{
    /// <summary>The first day of the series (a Monday).</summary>
    public static readonly DateOnly Start = new(2026, 7, 6);

    /// <summary>The index of the day with the event.</summary>
    public const int EventDay = 40;

    /// <summary>
    /// <paramref name="days"/> weekdays from <see cref="Start"/>: the close drifts by at most 0.4 % a day around 1000 and volume is about 100,000. Day <see cref="EventDay"/> opens at
    /// <paramref name="ratio"/> times the previous close on <paramref name="volumeMultiple"/> times the volume and the days after follow the new level. <paramref name="reverseAt"/>
    /// brings the price back to 99 % of the old level on that day.
    /// </summary>
    public static List<SeriesDay> Series(int days = 60, double ratio = 1.0, double volumeMultiple = 1.0, int? reverseAt = null)
    {
        var list = new List<SeriesDay>();
        var date = Start;
        var close = 1000.0;
        var old = 1000.0;
        for (var i = 0; i < days; i++)
        {
            var open = close;
            close = open * (1 + (((i * 7) % 9) - 4) / 1000.0);
            long volume = 100_000 + (((i * 13) % 11) - 5) * 1000L;
            if (i == EventDay && ratio != 1.0)
            {
                old = open;
                open *= ratio;
                close = open * 1.001;
                volume = (long)(100_000 * volumeMultiple);
            }

            if (reverseAt == i)
            {
                open = old * 0.99;
                close = old * 0.99;
            }

            var day = new SeriesDay(date, Math.Round(open, 2), Math.Round(Math.Max(open, close) * 1.002, 2), Math.Round(Math.Min(open, close) * 0.998, 2), Math.Round(close, 2), volume);
            list.Add(day);
            close = day.Close;
            date = date.AddDays(date.DayOfWeek == DayOfWeek.Friday ? 3 : 1);
        }

        return list;
    }

    public static List<Bar> Bars(IEnumerable<SeriesDay> series)
    {
        var bars = new List<Bar>();
        foreach (var day in series)
        {
            bars.Add(new Bar(BarFixtures.Epoch(day.Date, 9 * 60 + 15), day.Open, day.High, day.Low, day.Open, day.Volume));
            bars.Add(new Bar(BarFixtures.Epoch(day.Date, 15 * 60 + 29), day.Close, day.Close, day.Close, day.Close, 0));
        }

        return bars;
    }
}
