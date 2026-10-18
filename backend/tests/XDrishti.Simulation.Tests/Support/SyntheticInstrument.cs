using XDrishti.Domain.Market.Bars;

namespace XDrishti.Simulation.Tests.Support;

/// <summary>A synthetic instrument for the memory test and the benchmark: consecutive weekday sessions of 375 seeded minutes (a random walk in multiples of 0.25 around 100, no flagged day), as many as the minutes asked for need.</summary>
internal static class SyntheticInstrument
{
    /// <summary>The first date of the instrument (a Monday).</summary>
    public static readonly DateOnly Start = new(2024, 1, 1);

    /// <summary>The bars of <paramref name="sessions"/> weekday sessions; the generator seeds each session from <paramref name="seed"/> and its number.</summary>
    public static (Bar[] Bars, DateOnly Last) Sessions(int sessions, ulong seed)
    {
        var bars = new List<Bar>(sessions * SessionClock.RegularBars);
        var date = Start;
        for (var i = 0; i < sessions; i++)
        {
            var d = date;
            bars.AddRange(SeededSeries.Of((seed * 1_000_003) + (ulong)i).Take(SessionClock.RegularBars).Select((b, m) => b with { EpochSeconds = Day.Epoch(d, SessionClock.OpenMinute + m) }));
            date = date.DayOfWeek == DayOfWeek.Friday ? date.AddDays(3) : date.AddDays(1);
        }

        return ([.. bars], bars.Count == 0 ? Start : SessionClock.DateOf(bars[^1].EpochSeconds));
    }
}
