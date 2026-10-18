using XDrishti.Domain.Market.Bars;

namespace XDrishti.Simulation.Tests.Support;

/// <summary>The input of the explicit benchmarks: 250 seeded sessions, 93,750 minutes, on consecutive calendar days (the frame does not mind weekends).</summary>
internal static class BenchmarkSessions
{
    public const int Sessions = 250;

    public static Bar[] Minutes() =>
    [
        .. Enumerable.Range(0, Sessions).SelectMany(i =>
        {
            var date = new DateOnly(2026, 1, 5).AddDays(i);
            return SeededSeries.Of((ulong)(1000 + i)).Take(375).Select((b, m) => b with { EpochSeconds = Day.Epoch(date, SessionClock.OpenMinute + m) });
        }),
    ];
}
