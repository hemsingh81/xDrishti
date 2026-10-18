using XDrishti.Domain.Market.Bars;

namespace XDrishti.Simulation.Tests.Support;

/// <summary>
/// The days of the replay tests (spec P3-04, criteria): the fixture days of P3-03's table, dense, with the date shifted to the one given (D9), and the flat days, the short days and the evening sessions of the session table.
/// Volume is 1,000 per minute as everywhere. The rows are those of <see cref="Fixtures"/>; they are written again here so that this file does not change the fixtures of the other stages.
/// </summary>
internal static class ReplayDays
{
    public static readonly DateOnly D1 = new(2026, 3, 2);

    public static readonly DateOnly D2 = new(2026, 3, 3);

    public static readonly DateOnly D3 = new(2026, 3, 4);

    public static readonly DateOnly D4 = new(2026, 3, 5);

    public static readonly DateOnly D5 = new(2026, 3, 6);

    /// <summary>A dense day, 09:15 to 15:29, flat at the price.</summary>
    public static Bar[] Flat(DateOnly date, double price) => Day.Dense(date).Start(price).Bars();

    /// <summary>F1: BUY entry 100.50 at 09:45, stop 98.00, target 105.50, exit at the target at 11:10, R +2.0.</summary>
    public static Bar[] F1(DateOnly date) => Day.P(date).B().Set("10:05 100.50/100.50/99.50/100.50", "10:30 100.50/104.50/100.50/104.00", "11:10 104.00/105.50/103.50/105.00").Bars();

    /// <summary>F2: the same entry, stopped out at 10:50, R -1.0.</summary>
    public static Bar[] F2(DateOnly date) => Day.P(date).B().Set("10:20 100.50/101.50/100.50/101.00", "10:50 101.00/102.00/97.50/98.50").Bars();

    /// <summary>F6: F5 ends after 15:09 (355 bars, 94.7 % of 375): the position is still open when the data ends.</summary>
    public static Bar[] F6(DateOnly date) =>
        Day.P(date).B().Set("10:00 100.50/102.00/100.50/102.00", "11:40 102.00/102.00/99.00/101.50", "13:00 101.50/104.50/101.50/103.00", "15:15 103.00/106.00/97.00/103.00").EndsAfter("15:09").Bars();

    /// <summary>Every price of the bars multiplied by the factor (exact for the factors the tests use).</summary>
    public static Bar[] Scaled(IEnumerable<Bar> bars, double factor, long volumeFactor = 1) =>
        [.. bars.Select(b => b with { Open = b.Open * factor, High = b.High * factor, Low = b.Low * factor, Close = b.Close * factor, Volume = b.Volume * volumeFactor })];

    /// <summary><paramref name="count"/> flat bars of one price from the clock time <paramref name="from"/> on, one per minute, on the date.</summary>
    public static Bar[] Minutes(DateOnly date, string from, int count, double price = 99.0)
    {
        var start = Day.Minute(from);
        return [.. Enumerable.Range(0, count).Select(i => new Bar(Day.Epoch(date, start + i), price, price, price, price, Day.MinuteVolume))];
    }

    /// <summary>The bars of several days in one list (in the order given; the caller keeps them ascending).</summary>
    public static Bar[] Join(params Bar[][] days) => [.. days.SelectMany(d => d)];
}
