using XDrishti.Domain.Market.Bars;

namespace XDrishti.Tests.Shared;

/// <summary>
/// The banded sessions and prefixes of the VWAP_PULLBACK fixtures (spec P3-05, D9): a banded session trades at one level <c>c</c>, every 15-minute bar of it is <c>c / c+0.5 / c-0.5 / c</c> (true range exactly 1.0, volume 1,000 a minute),
/// so the ATR is exactly 1.00 and the hourly and daily trend values can be recomputed with plain arithmetic. PU is 21 sessions that climb from 100.50 to 110.50, PD the same sessions reflected about 110.50 (120.50 down to 110.50).
/// </summary>
internal static class BandedDays
{
    /// <summary>The level the up and down prefixes both end at.</summary>
    public const double Pivot = 110.50;

    /// <summary>The weekdays before <paramref name="date"/>, <paramref name="count"/> of them, oldest first.</summary>
    public static DateOnly[] SessionsBefore(DateOnly date, int count)
    {
        var dates = new List<DateOnly>();
        var cursor = date;
        while (dates.Count < count)
        {
            cursor = cursor.AddDays(-1);
            if (cursor.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                dates.Add(cursor);
            }
        }

        dates.Reverse();
        return [.. dates];
    }

    /// <summary>
    /// A banded session on the date: bars of 15 minutes <c>c / c+0.5 / c-0.5 / c</c> from 09:15 to 15:29. With a <paramref name="stubOffset"/> the last bar (15:15, the partial hourly bar) is banded around <c>c + offset</c> instead, so that the
    /// stub carries a value of its own (the seeded series use it to make the hourly bucket rule matter).
    /// </summary>
    public static Day Session(DateOnly date, double level, double stubOffset = 0)
    {
        var day = Day.Dense(date).Start(level);
        for (var minute = 9 * 60 + 15; minute < 15 * 60 + 30; minute += 15)
        {
            var c = minute == 15 * 60 + 15 ? level + stubOffset : level;
            day.Set(Day.Clock(minute) + " " + F(c) + "/" + F(c + 0.5) + "/" + F(c - 0.5) + "/" + F(c));
        }

        return day;
    }

    /// <summary>The up-trend prefix PU: <paramref name="sessions"/> sessions ending the day before <paramref name="fixtureDate"/>; session k (1 to 21) trades at 100 + 0.5 k.</summary>
    public static Bar[] Up(DateOnly fixtureDate, int sessions = 21)
    {
        var dates = SessionsBefore(fixtureDate, sessions);
        return Day.Join([.. dates.Select((d, i) => Session(d, Pivot - 0.5 * (sessions - 1 - i)))]);
    }

    /// <summary>The down-trend prefix PD: the mirror of PU about 110.50 (120.50 down to 110.50 for 21 sessions).</summary>
    public static Bar[] Down(DateOnly fixtureDate, int sessions = 21)
    {
        var dates = SessionsBefore(fixtureDate, sessions);
        return Day.Join([.. dates.Select((d, i) => Session(d, Pivot + 0.5 * (sessions - 1 - i)))]);
    }

    /// <summary>The mirror helper: every price becomes 221 - price (open stays the open, the high and the low swap), volumes and epochs are kept.</summary>
    public static Bar[] Mirror(IEnumerable<Bar> bars) =>
        [.. bars.Select(b => new Bar(b.EpochSeconds, 2 * Pivot - b.Open, 2 * Pivot - b.Low, 2 * Pivot - b.High, 2 * Pivot - b.Close, b.Volume))];

    /// <summary>The six bars of V1 (D9) at the first bar-start <paramref name="firstBar"/> (09:15 for V1 itself) written as 15-minute rows on <paramref name="day"/>.</summary>
    public static Day V1Bars(Day day, string firstBar = "09:15")
    {
        string[] rows =
        [
            "110.50/111.50/110.50/111.25",
            "111.25/112.25/111.25/112.00",
            "112.00/113.00/112.00/112.75",
            "112.75/113.75/112.75/113.50",
            "113.50/113.50/112.50/113.00",
            "113.00/114.00/113.00/113.75",
        ];
        var start = Day.Minute(firstBar);
        for (var i = 0; i < rows.Length; i++)
        {
            day.Set(Day.Clock(start + 15 * i) + " " + rows[i]);
        }

        return day;
    }

    private static string F(double value) => value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
}
