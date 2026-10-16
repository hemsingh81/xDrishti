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

    /// <summary>
    /// A long, cheap series for the weekly and monthly lanes: <paramref name="calendarDays"/> consecutive India dates from a seeded day between mid-2026 and late 2028 (so year ends and a leap February occur). A weekday is a
    /// holiday (8%, returned with the bars) or a session of a few bars at random minutes — it ends at the 15:29 close three times in four, otherwise earlier, and the next date has to close it —, of a single bar, or, for
    /// one day in twelve, of all 375 minutes. A Saturday has a short session one time in four and a Sunday a Muhurat evening one time in twenty. Prices are a random walk; some volumes are zero. The same seed always gives the
    /// same series. The builder tests use <see cref="BarFixtures.RandomPeriods"/> instead (mostly whole sessions, so that periods are complete and their bar counts exact).
    /// </summary>
    public static PeriodSeries Thin(int seed, int calendarDays)
    {
        var random = new Random(seed);
        var date = new DateOnly(2026, 6, 1).AddDays(random.Next(0, 900));
        var bars = new List<Bar>();
        var holidays = new HashSet<DateOnly>();
        var price = 100 + random.NextDouble() * 4_900;
        for (var i = 0; i < calendarDays; i++, date = date.AddDays(1))
        {
            IEnumerable<int> minutes;
            if (date.DayOfWeek == DayOfWeek.Saturday)
            {
                minutes = random.NextDouble() < 0.25 ? Enumerable.Range(Minutes.Open, random.Next(1, 40)) : [];
            }
            else if (date.DayOfWeek == DayOfWeek.Sunday)
            {
                minutes = random.NextDouble() < 0.05 ? Enumerable.Range(Minutes.At(18, 15), random.Next(1, 60)) : [];
            }
            else if (random.NextDouble() < 0.08)
            {
                holidays.Add(date);
                minutes = [];
            }
            else
            {
                minutes = WeekdayMinutes(random);
            }

            foreach (var minute in minutes)
            {
                var open = price;
                var close = Math.Max(1, open + (random.NextDouble() - 0.5) * price * 0.006);
                var high = Math.Max(open, close) + random.NextDouble() * price * 0.001;
                var low = Math.Max(0.5, Math.Min(open, close) - random.NextDouble() * price * 0.001);
                bars.Add(Minutes.Bar(date, minute, open, high, low, close, random.NextDouble() < 0.05 ? 0 : random.Next(1, 80_000)));
                price = close;
            }
        }

        return new PeriodSeries(bars, holidays);
    }

    private static List<int> WeekdayMinutes(Random random)
    {
        var roll = random.NextDouble();
        if (roll < 1.0 / 12)
        {
            return [.. Enumerable.Range(Minutes.Open, SessionClock.RegularBars)];
        }

        if (roll < 0.2)
        {
            return [random.Next(Minutes.Open, SessionClock.CloseMinute)];
        }

        var minutes = new SortedSet<int>();
        if (random.NextDouble() < 0.75)
        {
            minutes.Add(SessionClock.CloseMinute - 1);
        }

        var count = random.Next(2, 13);
        while (minutes.Count < count)
        {
            minutes.Add(random.Next(Minutes.Open, SessionClock.CloseMinute));
        }

        return [.. minutes];
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
