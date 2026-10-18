using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Replay;
using XDrishti.Simulation.Running;

namespace XDrishti.Simulation.Tests.Support;

/// <summary>An invented instrument for the window properties of the replay: nine to fourteen dense regular sessions on consecutive weekdays, some nights flagged (an open more than 20 % from the previous close).</summary>
internal sealed record SeededReplaySeries(int Seed, IReadOnlyList<Bar> Bars, List<DateOnly> Dates, IReadOnlySet<DateOnly> FlaggedDates)
{
    /// <summary>The bars up to the end of <paramref name="last"/> (the read ends at <c>To</c>).</summary>
    public Bar[] Through(DateOnly last) => [.. Bars.Where(b => SessionClock.DateOf(b.EpochSeconds) <= last)];

    /// <summary>The bars of the sessions from <paramref name="first"/> on.</summary>
    public Bar[] From(DateOnly first) => [.. Bars.Where(b => SessionClock.DateOf(b.EpochSeconds) >= first)];
}

/// <summary>One call of <c>Simulator.Run</c> seen by the replay: the list it was given and its result.</summary>
internal sealed record SegmentRun(IReadOnlyList<Bar> List, SimulationResult Result);

internal static class SeededReplay
{
    public const int Count = 100;

    private static readonly Lazy<IReadOnlyList<SeededReplaySeries>> AllSeries = new(() => [.. Enumerable.Range(1, Count).Select(Make)]);

    public static IReadOnlyList<SeededReplaySeries> All => AllSeries.Value;

    public static SeededReplaySeries Make(int seed)
    {
        var rng = new SeededSeries(((ulong)seed * 15_485_863UL) + 7UL);
        var sessions = 9 + rng.Int(6);
        var price = Math.Exp(Math.Log(50) + (rng.Unit() * (Math.Log(2_000) - Math.Log(50))));
        var bars = new List<Bar>();
        var dates = new List<DateOnly>();
        var flagged = new HashSet<DateOnly>();
        var date = TestCalendars.Mon;
        for (var s = 0; s < sessions; s++)
        {
            while (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                date = date.AddDays(1);
            }

            dates.Add(date);
            if (s > 0)
            {
                var roll = rng.Unit();
                if (s >= 3 && roll < 0.10)
                {
                    price *= rng.Unit() < 0.5 ? 0.55 + (rng.Unit() * 0.2) : 1.3 + (rng.Unit() * 0.3);
                    flagged.Add(date);
                }
                else if (roll < 0.35)
                {
                    price *= 1 + ((rng.Unit() < 0.5 ? -1 : 1) * (0.002 + (rng.Unit() * 0.06)));
                }
            }

            var drift = (rng.Unit() - 0.45) * 0.0002;
            for (var minute = SessionClock.OpenMinute; minute < SessionClock.CloseMinute; minute++)
            {
                var open = Snap(price);
                var close = Snap(open * (1 + drift + ((rng.Unit() - 0.5) * 0.0016)));
                var high = Math.Max(Snap(Math.Max(open, close) * (1 + (rng.Unit() * 0.0004))), Math.Max(open, close));
                var low = Math.Min(Snap(Math.Min(open, close) * (1 - (rng.Unit() * 0.0004))), Math.Min(open, close));
                bars.Add(new Bar(Day.Epoch(date, minute), open, high, low, close, Day.MinuteVolume));
                price = close;
            }

            date = date.AddDays(1);
        }

        return new SeededReplaySeries(seed, bars, dates, flagged);
    }

    private static double Snap(double price) => Math.Max(0.05, Math.Round(price / 0.05) * 0.05);

    /// <summary>The replay of the strategy over the instrument with the observer of the tests: the record and every call of <c>Simulator.Run</c>.</summary>
    public static (InstrumentReplay Replay, List<SegmentRun> Segments) Replay(PreparedStrategy strategy, IReadOnlyList<Bar> bars, DateOnly from, DateOnly to)
    {
        var segments = new List<SegmentRun>();
        var input = Rep.Input(bars, from, to, calendar: TestCalendars.DefaultWeek());
        var replay = InstrumentReplayer.ReplayCore(strategy, input, (list, result) => segments.Add(new SegmentRun(list, result)));
        ReplayIdentities.ShouldHold(replay, "seed replay");
        return (replay, segments);
    }
}
