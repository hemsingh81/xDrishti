using System.Globalization;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Tests.Shared;

/// <summary>
/// The worked daily series of P3-06 (D11), as <see cref="Day"/>s: H60 (60 flat sessions, a ramp from 85.00 to 99.75), BASE10 (ten sessions of a base), S (the signal day) and the continuations T1 to T5. Session numbers count from 0 on
/// Monday 2026-01-05, weekdays only and no holiday (the dates are invented): H60 is sessions 0 to 59, B1 to B10 sessions 60 to 69, S session 70 (Monday 2026-04-13), T1 to T5 sessions 71 to 75. A day is dense (375 minutes), opens at its open,
/// has its high, low and close in the 10:00 minute and repeats the close after it; volume is 1,000 a minute (375,000 a day) unless a multiple is given.
/// </summary>
internal static class BaseDays
{
    public static readonly DateOnly First = new(2026, 1, 5);

    private static readonly (double O, double H, double L, double C)[] BaseRows =
    [
        (99.75, 100.30, 99.60, 100.00),
        (100.00, 101.20, 99.90, 101.00),
        (101.00, 101.10, 100.00, 100.20),
        (100.20, 101.00, 100.10, 100.80),
        (100.80, 100.90, 99.40, 100.40),
        (100.40, 101.10, 100.30, 100.90),
        (100.90, 101.00, 100.20, 100.30),
        (100.30, 100.90, 100.20, 100.70),
        (100.70, 100.80, 100.30, 100.50),
        (100.50, 100.80, 100.40, 100.60),
    ];

    /// <summary>The weekday number <paramref name="session"/> counting from <see cref="First"/> (no holiday).</summary>
    public static DateOnly Date(int session)
    {
        var weeks = Math.DivRem(session, 5, out var rest);
        return First.AddDays((7 * weeks) + rest);
    }

    /// <summary>One day: open <paramref name="o"/>, then the 10:00 minute <paramref name="o"/>/<paramref name="h"/>/<paramref name="l"/>/<paramref name="c"/>; volume <paramref name="perMinute"/> a minute.</summary>
    public static Day Ohlc(int session, double o, double h, double l, double c, long perMinute = Day.MinuteVolume) =>
        Day.Dense(Date(session)).Start(o).Set(string.Create(CultureInfo.InvariantCulture, $"10:00 {o}/{h}/{l}/{c}")).Volume(perMinute);

    /// <summary>One flat day at the price.</summary>
    public static Day Flat(int session, double price, long perMinute = Day.MinuteVolume) => Day.Dense(Date(session)).Start(price).Volume(perMinute);

    /// <summary>H60: sessions 0 to 59, session i flat at 85.00 + 0.25 i.</summary>
    public static Day[] Ramp() => [.. Enumerable.Range(0, 60).Select(i => Flat(i, 85.0 + (0.25 * i)))];

    /// <summary>BASE10: sessions 60 to 69 (B1 to B10).</summary>
    public static Day[] Base() => [.. BaseRows.Select((r, i) => Ohlc(60 + i, r.O, r.H, r.L, r.C))];

    /// <summary>S: session 70, <c>100.70 / 102.30 / 100.60 / 102.00</c>, volume <paramref name="volumeFactor"/> times the usual (1.6 by default).</summary>
    public static Day Signal(double volumeFactor = 1.6, double close = 102.00) => Ohlc(70, 100.70, 102.30, 100.60, close, (long)(Day.MinuteVolume * volumeFactor));

    /// <summary>H60, BASE10 and S: 71 sessions.</summary>
    public static Day[] ThroughSignal(double volumeFactor = 1.6, double close = 102.00) => [.. Ramp(), .. Base(), Signal(volumeFactor, close)];

    /// <summary>The bars of the days.</summary>
    public static Bar[] Bars(IEnumerable<Day> days) => Day.Join([.. days]);
}
