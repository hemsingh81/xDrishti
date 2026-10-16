using XDrishti.Domain.Market.Bars;

namespace XDrishti.Strategies.Tests.Support.Oracle;

/// <summary>The kind of session a series is made of.</summary>
internal enum SeriesShape
{
    /// <summary>Every minute of a few sessions: the buckets of the higher timeframes are whole.</summary>
    Dense,

    /// <summary>Every minute of a few sessions except a few at random (also at the open): buckets are incomplete here and there.</summary>
    Gappy,

    /// <summary>Many sessions of only a few random minutes each (the last minute of the session most of the time): cheap to run over weeks and months, with incomplete buckets everywhere.</summary>
    Sparse,
}

/// <summary>
/// Seeded random 1-minute series for the oracle and look-ahead tests: several sessions over weeks (weekends skipped, a holiday now and then), prices on a tick so that equal prices and flat bars happen (equality in a
/// comparison or at the previous bar of a crossing is then a real case, not a coincidence of zero probability), gaps at the open, a spike now and then, zero volumes. Time-ordered, prices above zero. Nothing is read from a file.
/// </summary>
internal static class OracleSeries
{
    /// <summary>The kind of series the oracle runs on for an evaluation timeframe: whole or gappy sessions for the intraday ones, sparse sessions over months for the calendar ones.</summary>
    public static Bar[] For(int seed, Timeframe evaluation, bool light = false)
    {
        var random = new Random(seed);
        switch (evaluation)
        {
            case Timeframe.D1:
                return Build(seed, SeriesShape.Sparse, random.Next(40, 61));
            case Timeframe.W1:
                return Build(seed, SeriesShape.Sparse, random.Next(80, 111));
            case Timeframe.Mo1:
                return Build(seed, SeriesShape.Sparse, random.Next(130, 171));
            default:
                {
                    var shape = (SeriesShape)(seed % 3);
                    var sessions = shape == SeriesShape.Sparse ? random.Next(18, 29) : light ? 2 : random.Next(2, 4);
                    var bars = Build(seed, shape, sessions);
                    return shape == SeriesShape.Gappy ? WithDisorder(bars, seed) : bars;
                }
        }
    }

    public static Bar[] Build(int seed, SeriesShape shape, int sessions)
    {
        var random = new Random(seed * 7919 + 13);
        double[] volatilities = [0.002, 0.01, 0.03];
        double[] ticks = [0.05, 0.5, 5.0];
        var volatility = volatilities[random.Next(volatilities.Length)];
        var tick = ticks[random.Next(ticks.Length)];
        var price = Round(100 + random.NextDouble() * 1_900, tick);
        var previousVolume = 100L;
        var date = Clock.Mon.AddDays(7 * random.Next(0, 4));
        var bars = new List<Bar>();
        for (var s = 0; s < sessions; s++)
        {
            while (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday || (s > 0 && random.Next(100) < 5 && date.DayOfWeek != DayOfWeek.Monday))
            {
                date = date.AddDays(1);
            }

            foreach (var minute in MinutesOf(random, shape))
            {
                var open = random.Next(10) < 8 ? price : Round(price * (1 + (random.NextDouble() - 0.5) * volatility * 2), tick);
                open = Math.Max(tick, open);
                var close = Math.Max(tick, Round(open * (1 + (random.NextDouble() - 0.5) * volatility), tick));
                var high = Math.Max(open, close) * (1 + random.NextDouble() * volatility / 2);
                var low = Math.Min(open, close) * (1 - random.NextDouble() * volatility / 2);
                var roll = random.Next(100);
                if (roll < 4)
                {
                    close = open;
                    high = open;
                    low = open;
                }
                else if (roll < 5)
                {
                    high = Math.Max(open, close) * (1 + volatility * 5);
                }

                high = Math.Max(Round(high, tick), Math.Max(open, close));
                low = Math.Max(tick, Math.Min(Round(low, tick), Math.Min(open, close)));
                var volume = random.Next(100) < 5 ? 0 : random.Next(100) < 10 ? previousVolume : random.Next(1, 3_000);
                previousVolume = volume;
                bars.Add(Clock.Bar(date, Clock.Open + minute, open, high, low, close, volume));
                price = close;
            }

            date = date.AddDays(1);
        }

        return [.. bars];
    }

    /// <summary>
    /// Now and then (and surely after the 8th and the 32nd bar) a duplicate of the bar just fed, or a bar from three minutes earlier, right after it: the frame's ordering guard skips such a bar (it counts it in <c>IgnoredBars</c> and changes nothing), so a series
    /// with them has steps at which nothing happened.
    /// </summary>
    private static Bar[] WithDisorder(Bar[] bars, int seed)
    {
        var random = new Random(seed ^ 0x5A5A);
        var result = new List<Bar>(bars.Length + 8);
        for (var i = 0; i < bars.Length; i++)
        {
            result.Add(bars[i]);
            if (i == 7 || i == 31 || (i > 3 && random.Next(150) == 0))
            {
                result.Add(random.Next(2) == 0 ? bars[i] : bars[i - 3]);
            }
        }

        return [.. result];
    }

    private static double Round(double value, double tick) => Math.Round(value / tick) * tick;

    /// <summary>The minutes after 09:15 of a session (0 to 374), in order.</summary>
    private static List<int> MinutesOf(Random random, SeriesShape shape)
    {
        switch (shape)
        {
            case SeriesShape.Dense:
                return [.. Enumerable.Range(0, 375)];
            case SeriesShape.Gappy:
                {
                    var kept = Enumerable.Range(0, 375).Where(m => random.Next(100) >= 3).ToList();
                    return kept.Count < 20 ? [.. Enumerable.Range(0, 375)] : kept;
                }

            default:
                {
                    var minutes = new SortedSet<int>();
                    var count = random.Next(3, 9);
                    while (minutes.Count < count)
                    {
                        minutes.Add(random.Next(0, 375));
                    }

                    if (random.Next(100) < 75)
                    {
                        minutes.Add(374);
                    }

                    if (random.Next(100) < 50)
                    {
                        minutes.Add(0);
                    }

                    return [.. minutes];
                }
        }
    }
}
