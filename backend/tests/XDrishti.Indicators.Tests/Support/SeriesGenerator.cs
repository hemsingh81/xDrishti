using XDrishti.Domain.Market.Bars;

namespace XDrishti.Indicators.Tests.Support;

/// <summary>
/// Seeded random 1-minute series for the property tests: consecutive weekday sessions (09:15 to 15:29) with missing minutes, sessions that end early, flat bars, zero-volume bars, gap-ups and gap-downs at the
/// open, and optionally a Saturday session and a Muhurat evening. The same seed always gives the same bars.
/// </summary>
internal static class SeriesGenerator
{
    public static List<Bar> Random(int seed, int minBars = 1_000, int maxBars = 5_000, bool specialSessions = false)
    {
        var random = new Random(seed);
        var target = random.Next(minBars, maxBars + 1);
        var bars = new List<Bar>(target + 400);
        var date = new DateOnly(2026, 1, 5).AddDays(random.Next(0, 5));
        var price = 100 + random.NextDouble() * 4_900;

        while (bars.Count < target)
        {
            if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                date = date.AddDays(1);
                continue;
            }

            price = Math.Max(1, price * (1 + (random.NextDouble() < 0.3 ? (random.NextDouble() - 0.5) * 0.08 : (random.NextDouble() - 0.5) * 0.004)));
            var count = random.NextDouble() < 0.15 ? random.Next(150, 375) : 375;
            price = Session(random, bars, date, Minutes.Open, count, price, gapProbability: 0.02);

            if (specialSessions && date.DayOfWeek == DayOfWeek.Friday)
            {
                // A Saturday session (after most Fridays) and a Muhurat-style Sunday evening (after about half): dates of their own, after the Friday.
                if (random.NextDouble() < 0.6)
                {
                    price = Session(random, bars, date.AddDays(1), Minutes.Open, random.Next(20, 80), price, gapProbability: 0.02);
                }

                if (random.NextDouble() < 0.5)
                {
                    price = Session(random, bars, date.AddDays(2), Minutes.At(18, 15), 60, price, gapProbability: 0.02);
                }
            }

            date = date.AddDays(1);
        }

        bars.Sort((a, b) => a.EpochSeconds.CompareTo(b.EpochSeconds));
        return bars;
    }

    /// <summary>A gap-free regular session of 375 bars (a plain random walk), for warm-up measurements.</summary>
    public static List<Bar> Sessions(int seed, int days)
    {
        var random = new Random(seed);
        var bars = new List<Bar>(days * 375);
        var date = new DateOnly(2026, 2, 2);
        var price = 1_000.0;
        for (var d = 0; d < days; d++)
        {
            while (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                date = date.AddDays(1);
            }

            price = Session(random, bars, date, Minutes.Open, 375, price, gapProbability: 0, plain: true);
            date = date.AddDays(1);
        }

        return bars;
    }

    private static double Session(Random random, List<Bar> bars, DateOnly date, int startMinute, int count, double price, double gapProbability, bool plain = false)
    {
        var open = price;
        for (var i = 0; i < count; i++)
        {
            var change = (random.NextDouble() - 0.5) * price * 0.002;
            var close = Math.Max(1, open + change);
            var flat = !plain && random.NextDouble() < 0.05;
            var high = flat ? open : Math.Max(open, close) + random.NextDouble() * price * 0.0008;
            var low = flat ? open : Math.Max(0.5, Math.Min(open, close) - random.NextDouble() * price * 0.0008);
            if (flat)
            {
                close = open;
            }

            var volume = !plain && random.NextDouble() < 0.05 ? 0L : plain ? 1 + random.Next(1, 50_000) : random.Next(0, 80_000) * (random.NextDouble() < 0.02 ? 20L : 1L);
            if (plain || random.NextDouble() >= gapProbability)
            {
                bars.Add(Minutes.Bar(date, startMinute + i, open, high, low, close, volume));
            }

            open = close;
        }

        return open;
    }
}
