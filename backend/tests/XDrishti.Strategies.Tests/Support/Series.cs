using XDrishti.Domain.Market.Bars;

namespace XDrishti.Strategies.Tests.Support;

/// <summary>Seeded 1-minute series for the property, allocation and benchmark tests: the same seed always gives the same bars, and nothing is read from a file.</summary>
internal static class Series
{
    /// <summary>
    /// Consecutive weekday sessions of 375 minutes (09:15 to 15:29) from <paramref name="first"/> (a weekday): a random walk with a small move per minute and a gap at the open, prices well above zero, volumes of 50
    /// to 1,000 with a zero now and then. Time-ordered without a gap inside a session.
    /// </summary>
    public static Bar[] Sessions(int seed, int sessions, DateOnly? first = null)
    {
        var random = new Random(seed);
        var date = first ?? Clock.Mon;
        var bars = new List<Bar>(sessions * 375);
        var price = 200 + random.NextDouble() * 1_800;
        for (var s = 0; s < sessions; s++)
        {
            while (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                date = date.AddDays(1);
            }

            price = Math.Max(5, price * (1 + (random.NextDouble() - 0.5) * 0.02));
            for (var m = 0; m < 375; m++)
            {
                var open = price;
                price = Math.Max(5, price * (1 + (random.NextDouble() - 0.5) * 0.004));
                var high = Math.Max(open, price) * (1 + random.NextDouble() * 0.001);
                var low = Math.Min(open, price) * (1 - random.NextDouble() * 0.001);
                var volume = random.Next(0, 40) == 0 ? 0 : random.Next(50, 1_000);
                bars.Add(Clock.Bar(date, Clock.Open + m, open, high, low, price, volume));
            }

            date = date.AddDays(1);
        }

        return [.. bars];
    }

    /// <summary>The minutes of a sparse session: 09:15, 11:00, 13:00 and 15:29, the last minute of the session (so that a day, a week and a month close at the end of their last session).</summary>
    public static readonly int[] SparseMinutes = [Clock.Open, Clock.At(11, 0), Clock.At(13, 0), Clock.At(15, 29)];

    /// <summary>
    /// Consecutive weekday sessions of only four bars (<see cref="SparseMinutes"/>): minutes are missing, which the frame accepts (the buckets of a higher timeframe are then incomplete). The cheap way to run a frame over
    /// months for the features of the daily, weekly and monthly timeframes and the date-driven ones, which look at the day and not at the minute.
    /// </summary>
    public static Bar[] Sparse(int seed, int sessions, DateOnly? first = null)
    {
        var random = new Random(seed);
        var date = first ?? Clock.Mon;
        var bars = new List<Bar>(sessions * SparseMinutes.Length);
        var price = 200 + random.NextDouble() * 1_800;
        for (var s = 0; s < sessions; s++)
        {
            while (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                date = date.AddDays(1);
            }

            foreach (var minute in SparseMinutes)
            {
                var open = price;
                price = Math.Max(5, price * (1 + (random.NextDouble() - 0.5) * 0.03));
                var high = Math.Max(open, price) * (1 + random.NextDouble() * 0.005);
                var low = Math.Min(open, price) * (1 - random.NextDouble() * 0.005);
                bars.Add(Clock.Bar(date, minute, open, high, low, price, random.Next(0, 40) == 0 ? 0 : random.Next(50, 1_000)));
            }

            date = date.AddDays(1);
        }

        return [.. bars];
    }
}
