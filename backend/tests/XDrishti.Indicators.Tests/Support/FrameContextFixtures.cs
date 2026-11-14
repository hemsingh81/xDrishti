using XDrishti.Domain.Market.Bars;

namespace XDrishti.Indicators.Tests.Support;

/// <summary>What the frame tests of the calendar features share: minute bars on given dates, the ISO weekday from the platform's calendar, and frames of the date-driven features.</summary>
internal static class FrameContextFixtures
{
    /// <summary>Every calendar day from the first to the last, both included.</summary>
    public static List<DateOnly> Dates(DateOnly first, DateOnly last)
    {
        var dates = new List<DateOnly>();
        for (var date = first; date <= last; date = date.AddDays(1))
        {
            dates.Add(date);
        }

        return dates;
    }

    /// <summary>One bar at each of the minutes of the day on every date, in time order (a price that does not matter to the date-driven features).</summary>
    public static List<Bar> OnDates(IEnumerable<DateOnly> dates, params int[] minutesOfDay)
    {
        var bars = new List<Bar>();
        foreach (var date in dates)
        {
            foreach (var minute in minutesOfDay)
            {
                bars.Add(Minutes.Close(date, minute, 100 + minute % 7));
            }
        }

        return bars;
    }

    /// <summary>The 375 bars of a regular session, 09:15 to 15:29.</summary>
    public static List<Bar> Session(DateOnly date) => OnDates([date], [.. Enumerable.Range(Minutes.Open, 375)]);

    /// <summary>The India date of a bar, from the epoch with plain arithmetic on the platform's date type (independent of the library's day arithmetic).</summary>
    public static DateOnly DateOf(Bar bar) => DateOnly.FromDateTime(DateTime.UnixEpoch.AddSeconds(bar.EpochSeconds + 19_800));

    /// <summary>The ISO weekday (Monday 1 … Sunday 7) from the platform's day of week.</summary>
    public static int IsoWeekday(DateOnly date) => date.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)date.DayOfWeek;

    public static FeatureSpec[] Specs(IEnumerable<string> names) => [.. names.Select(n => new FeatureSpec(n))];

    /// <summary>A frame of the named date-driven features (each on 1m, no parameters) with the given expiry dates (an empty set of them when none is given, so that every name is valid).</summary>
    public static FeatureFrame Frame(IEnumerable<string> names, IReadOnlySet<DateOnly>? expiryDays = null, int historyDepth = FrameOptions.DefaultHistoryDepth) =>
        FeatureFrame.Create(Specs(names), new FrameOptions(null, historyDepth, expiryDays ?? new HashSet<DateOnly>())).Value;

    /// <summary>Steps the bars and records, after each step, the value and the history lags 1 and 7 of every handle as raw bits: two frames that did the same must give the same trace.</summary>
    public static List<long> Trace(FeatureFrame frame, IEnumerable<Bar> bars)
    {
        var bits = new List<long>();
        foreach (var bar in bars)
        {
            frame.Step(bar);
            for (var h = 0; h < frame.Count; h++)
            {
                bits.Add(BitConverter.DoubleToInt64Bits(frame[h]));
                bits.Add(BitConverter.DoubleToInt64Bits(frame.At(h, 1)));
                bits.Add(BitConverter.DoubleToInt64Bits(frame.At(h, 7)));
            }
        }

        return bits;
    }

    /// <summary>Every history lag the frame keeps (lag 0 is the value), and the ready flag, of every handle as raw bits: two frames that did the same must give the same list.</summary>
    public static List<long> Bits(FeatureFrame frame)
    {
        var bits = new List<long>(frame.Count * (frame.HistoryDepth + 1));
        for (var h = 0; h < frame.Count; h++)
        {
            for (var lag = 0; lag < frame.HistoryDepth; lag++)
            {
                bits.Add(BitConverter.DoubleToInt64Bits(frame.At(h, lag)));
            }

            bits.Add(frame.IsReady(h) ? 1 : 0);
        }

        return bits;
    }
}
