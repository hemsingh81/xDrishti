using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators;

namespace XDrishti.Strategies.Tests.Support.Oracle;

/// <summary>Seeded market contexts and expiry dates for the oracle and look-ahead tests, covering the dates of a series.</summary>
internal static class OracleContext
{
    /// <summary>
    /// A daily context that starts about nine weeks before the first bar and ends a week after the last, with phases that change the trend and the volatility. About one day in ten has no VIX and one in ten no breadth
    /// (a gap is never filled), so the context features are not ready on the date after such a day. Short periods (<see cref="Contexts.Small"/>) make it ready within a few weeks.
    /// </summary>
    public static MarketContext For(int seed, IReadOnlyList<Bar> bars) => Build(Days(seed, bars));

    /// <summary>The records <see cref="For"/> is built from, oldest first.</summary>
    public static List<ContextDay> Days(int seed, IReadOnlyList<Bar> bars)
    {
        var random = new Random(seed * 104_729 + 7);
        var first = DateOf(bars[0]);
        var last = DateOf(bars[^1]);
        var start = first.AddDays(-45);
        start = start.AddDays(-((int)start.DayOfWeek + 6) % 7);
        var days = new List<ContextDay>();
        var close = 20_000.0;
        for (var date = start; date <= last.AddDays(7); date = date.AddDays(1))
        {
            if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                continue;
            }

            var phase = days.Count / 18 % 4;
            var drift = phase switch { 0 => 0.008, 1 => 0.0, 2 => -0.008, _ => 0.006 };
            var spread = phase == 3 ? 0.03 : 0.006;
            close *= 1 + drift + (random.NextDouble() - 0.5) * 0.004;
            var high = close * (1 + random.NextDouble() * spread);
            var low = close * (1 - random.NextDouble() * spread);
            double? vix = random.Next(10) == 0 ? null : Math.Round(10 + phase * 4 + random.NextDouble() * 12, 2);
            double? breadth = random.Next(10) == 0 ? null : Math.Round(random.NextDouble() * 100, 1);
            days.Add(new ContextDay(date, high, low, close, vix, breadth));
        }

        return days;
    }

    /// <summary>The context of the records (the short periods of <see cref="Contexts.Small"/>).</summary>
    public static MarketContext Build(IEnumerable<ContextDay> days)
    {
        var created = MarketContext.Create(days, Contexts.Small);
        created.IsSuccess.ShouldBeTrue(created.IsFailure ? created.Error.Message : string.Empty);
        return created.Value;
    }

    /// <summary>The same date with every price scaled, a VIX that no generated day has (99.9) and a breadth of 3: a record that cannot be mistaken for the original and is still a possible day.</summary>
    public static ContextDay Altered(ContextDay day) => day with
    {
        IndexHigh = day.IndexHigh * 1.37,
        IndexLow = day.IndexLow * 1.37,
        IndexClose = day.IndexClose * 1.37,
        VixClose = 99.9,
        Breadth = 3,
    };

    /// <summary>The Thursdays of a long stretch around the series and a few other dates, as a set the frame may share.</summary>
    public static IReadOnlySet<DateOnly> ExpiryDays(int seed)
    {
        var random = new Random(seed * 31 + 5);
        var days = new HashSet<DateOnly>();
        for (var i = 0; i < 400; i++)
        {
            var date = Clock.Mon.AddDays(i - 40);
            if (date.DayOfWeek == DayOfWeek.Thursday || random.Next(100) < 4)
            {
                days.Add(date);
            }
        }

        return days;
    }

    private static DateOnly DateOf(Bar bar) => DateOnly.FromDateTime(bar.Start.UtcDateTime);
}
