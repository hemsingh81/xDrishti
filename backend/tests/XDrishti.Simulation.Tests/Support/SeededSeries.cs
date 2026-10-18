using XDrishti.Domain.Market.Bars;

namespace XDrishti.Simulation.Tests.Support;

/// <summary>
/// A tiny deterministic generator (SplitMix64: the same numbers on every machine and runtime, no <c>System.Random</c>) and the invented series of the properties: one to three consecutive sessions of 1-minute bars,
/// a random walk in multiples of 0.25 around 100 with a gap between sessions. Tuned so that ORB v1 breaks out of its opening range often enough on both sides for the properties to compare many candidates.
/// </summary>
internal sealed class SeededSeries(ulong seed)
{
    private ulong _state = seed;

    /// <summary>The next 64 random bits.</summary>
    public ulong Next()
    {
        _state += 0x9E3779B97F4A7C15UL;
        var z = _state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>An integer in [0, <paramref name="exclusiveMax"/>).</summary>
    public int Int(int exclusiveMax) => (int)(Next() % (ulong)exclusiveMax);

    /// <summary>A number in [0, 1).</summary>
    public double Unit() => (Next() >> 11) * (1.0 / (1UL << 53));

    /// <summary>The series of the seed: one to three sessions on consecutive weekdays from 2026-03-02.</summary>
    public static Bar[] Of(ulong seed)
    {
        var rng = new SeededSeries(seed);
        var sessions = 1 + rng.Int(3);
        var bars = new List<Bar>();
        var price = 100.0;
        var date = Day.Monday;
        for (var s = 0; s < sessions; s++)
        {
            price = Round(price + (rng.Unit() - 0.5) * 3);
            var drift = (rng.Unit() - 0.5) * 0.06;
            for (var minute = SessionClock.OpenMinute; minute < SessionClock.CloseMinute; minute++)
            {
                var open = price;
                var close = Round(open + drift + (rng.Unit() + rng.Unit() + rng.Unit() - 1.5) * 0.6);
                var high = Round(Math.Max(open, close) + rng.Unit() * 0.4);
                var low = Round(Math.Min(open, close) - rng.Unit() * 0.4);
                bars.Add(new Bar(Day.Epoch(date, minute), open, high, low, close, 500 + rng.Int(1500)));
                price = close;
            }

            date = date.DayOfWeek == DayOfWeek.Friday ? date.AddDays(3) : date.AddDays(1);
        }

        return [.. bars];
    }

    private static double Round(double value) => Math.Round(value * 4, MidpointRounding.AwayFromZero) / 4;
}
