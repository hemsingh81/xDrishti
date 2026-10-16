using XDrishti.Indicators;

namespace XDrishti.Strategies.Tests.Support;

/// <summary>Seeded daily market contexts for the tests of the context features: consecutive weekdays, an index that trends up, ranges and trends down in turn, with a VIX and a breadth on every day.</summary>
internal static class Contexts
{
    /// <summary>Short periods and a short percentile window: a series of 60 days is ready after 14 and every kind of day occurs in it (the options the Indicators tests use for the same purpose).</summary>
    public static readonly ContextOptions Small = new(TrendSmaPeriod: 6, TrendSlopeDays: 2, AdxPeriod: 3, AdxMin: 15, AtrPeriod: 3, PercentileWindow: 10);

    /// <summary>
    /// Weekdays from the Monday <paramref name="daysBefore"/> days before <paramref name="first"/> to a week after <paramref name="last"/>, so that the context is warm on the first date asked for and still has a day on the
    /// last. Phases of 25 days (up, range, down, up with a wide range …) make the trend and the volatility change.
    /// </summary>
    public static MarketContext Covering(DateOnly first, DateOnly last, int seed, int daysBefore = 60, ContextOptions? options = null)
    {
        var random = new Random(seed);
        var start = first.AddDays(-daysBefore);
        start = start.AddDays(-((int)start.DayOfWeek + 6) % 7);
        var days = new List<ContextDay>();
        var close = 20_000.0;
        for (var date = start; date <= last.AddDays(7); date = date.AddDays(1))
        {
            if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                continue;
            }

            var phase = days.Count / 25 % 4;
            var drift = phase switch { 0 => 0.008, 1 => 0.0, 2 => -0.008, _ => 0.006 };
            var spread = phase == 3 ? 0.03 : 0.006;
            close *= 1 + drift + (random.NextDouble() - 0.5) * 0.004;
            var high = close * (1 + random.NextDouble() * spread);
            var low = close * (1 - random.NextDouble() * spread);
            days.Add(new ContextDay(date, high, low, close, Math.Round(12 + random.NextDouble() * 10, 2), Math.Round(30 + random.NextDouble() * 40, 1)));
        }

        return MarketContext.Create(days, options ?? Small).Value;
    }
}
