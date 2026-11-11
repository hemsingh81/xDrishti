using XDrishti.Application.Abstractions.Market.Baskets;
using XDrishti.Application.Abstractions.OpeningBars;
using XDrishti.Application.OpeningBars;
using XDrishti.Domain.Baskets.Derived;

namespace XDrishti.Application.Tests.OpeningBars;

/// <summary>BK-03b criteria 3, 4 and 7: no look-ahead, complete windows, why a whole day is not formed, and the cap on the subscribed set.</summary>
public sealed class OpeningWindowsTests
{
    private static readonly DateOnly Day = new(2026, 10, 12);
    private static readonly Guid Id = Guid.NewGuid();

    private static DateTimeOffset At(int hour, int minute, int second = 0) => OpeningWindows.At(Day, new TimeOnly(hour, minute, second));

    private static MinuteBar Bar(DateTimeOffset minute, Guid? id = null) => new(id ?? Id, minute, 100m, 101m, 99m, 100m, 10, 1);

    private static IEnumerable<MinuteBar> Minutes(int from, int to) => Enumerable.Range(from, to - from).Select(m => Bar(At(9, 15).AddMinutes(m)));

    [Fact]
    public void The_bar_of_the_formation_minute_is_never_handed_to_the_engine_even_when_it_is_written()
    {
        var bars = Minutes(0, 6); // 09:15 to 09:20 inclusive

        var seen = OpeningWindows.Before(bars, At(9, 20)).ToList();

        seen.Select(b => b.MinuteStart.Minute).ShouldBe([15, 16, 17, 18, 19]);
    }

    [Fact]
    public void Over_random_bar_sets_no_input_starts_at_or_after_the_formation_time()
    {
        var random = new Random(7);
        for (var run = 0; run < 200; run++)
        {
            var bars = Enumerable.Range(0, 60).Select(_ => Bar(At(9, 15).AddMinutes(random.Next(0, 30)).AddSeconds(random.Next(0, 3)))).ToList();
            var formation = At(9, 15).AddMinutes(random.Next(1, 31));

            OpeningWindows.Before(bars, formation).ShouldAllBe(b => b.MinuteStart < formation);
        }
    }

    [Fact]
    public void A_window_is_complete_only_when_every_minute_from_the_open_has_a_bar()
    {
        OpeningWindows.IsComplete(Minutes(0, 5), Day, At(9, 20)).ShouldBeTrue();
        OpeningWindows.IsComplete(Minutes(0, 5).Where(b => b.MinuteStart != At(9, 17)), Day, At(9, 20)).ShouldBeFalse("the 09:17 minute is missing");
        OpeningWindows.IsComplete(Minutes(0, 5).Concat(Minutes(0, 5)), Day, At(9, 20)).ShouldBeTrue("a duplicate is not a second minute");
        OpeningWindows.IsComplete(Minutes(1, 6), Day, At(9, 20)).ShouldBeFalse("the 09:15 minute is missing");
        OpeningWindows.IsComplete([], Day, At(9, 20)).ShouldBeFalse();
    }

    public static TheoryData<string, OpeningBarsEvent[], string?> WholeDayCases() => new()
    {
        { "no events: the recorder was off or not running", [], OpeningWindows.NoLiveBars },
        { "started before the open, nothing else", [new(OpeningBarsEvent.Started, At(9, 14))], null },
        { "started exactly at the open", [new(OpeningBarsEvent.Started, At(9, 15))], null },
        { "started after the open: the first minute is partial", [new(OpeningBarsEvent.Started, At(9, 15, 20))], OpeningWindows.FeedGap },
        { "a drop inside the window", [new(OpeningBarsEvent.Started, At(9, 14)), new(OpeningBarsEvent.Gap, At(9, 17), At(9, 17, 30))], OpeningWindows.FeedGap },
        { "a gap still open", [new(OpeningBarsEvent.Started, At(9, 14)), new(OpeningBarsEvent.Gap, At(9, 18))], OpeningWindows.FeedGap },
        { "a gap that ended before the open", [new(OpeningBarsEvent.Started, At(9, 10)), new(OpeningBarsEvent.Gap, At(9, 12), At(9, 14))], null },
        { "a gap after the formation time does not matter", [new(OpeningBarsEvent.Started, At(9, 14)), new(OpeningBarsEvent.Gap, At(9, 21), At(9, 22))], null },
        { "a restart before the open is not a hole", [new(OpeningBarsEvent.Started, At(9, 10)), new(OpeningBarsEvent.Started, At(9, 13))], null },
        { "an open gap row and its later closing row before the open", [new(OpeningBarsEvent.Started, At(9, 10)), new(OpeningBarsEvent.Gap, At(9, 12)), new(OpeningBarsEvent.Gap, At(9, 12), At(9, 14))], null },
        { "an open gap row whose closing row is inside the window", [new(OpeningBarsEvent.Started, At(9, 14)), new(OpeningBarsEvent.Gap, At(9, 17)), new(OpeningBarsEvent.Gap, At(9, 17), At(9, 17, 30))], OpeningWindows.FeedGap },
        { "a clock skew", [new(OpeningBarsEvent.Started, At(9, 14)), new(OpeningBarsEvent.ClockSkew, At(9, 16))], OpeningWindows.ClockSkew },
        { "a restart is a hole", [new(OpeningBarsEvent.Started, At(9, 14)), new(OpeningBarsEvent.Started, At(9, 17))], OpeningWindows.FeedGap },
        { "low disk before the formation", [new(OpeningBarsEvent.Started, At(9, 14)), new(OpeningBarsEvent.LowDisk, At(9, 18))], OpeningWindows.LowDisk },
        { "low disk without a start", [new(OpeningBarsEvent.LowDisk, At(9, 12))], OpeningWindows.LowDisk },
        { "low disk after the formation does not matter", [new(OpeningBarsEvent.Started, At(9, 14)), new(OpeningBarsEvent.LowDisk, At(9, 25))], null },
    };

    [Theory]
    [MemberData(nameof(WholeDayCases))]
    public void A_whole_day_is_not_formed_for_a_named_reason_and_a_missing_input_is_never_a_pass(string why, OpeningBarsEvent[] events, string? reason)
    {
        _ = why;

        OpeningWindows.WholeReason(events, Day, At(9, 20)).ShouldBe(reason);
    }

    private static UniverseInstrument Share(string symbol, double volume) => new(Guid.NewGuid(), new InstrumentBars(symbol, DerivedFixturesBars(volume)));

    private static Bar[] DerivedFixturesBars(double volume) => [.. Enumerable.Range(0, 25).Select(i => new Bar(Day.AddDays(i - 30), 100, 101, 99, 100, volume))];

    [Fact]
    public void A_universe_of_450_under_a_cap_of_500_is_all_subscribed()
    {
        var universe = Enumerable.Range(0, 450).Select(i => Share($"S{i:D3}", i + 1)).ToList();

        var (kept, cut) = OpeningWindows.Subscribed(universe, 500);

        kept.Count.ShouldBe(450);
        cut.ShouldBeEmpty();
    }

    [Fact]
    public void Over_the_cap_the_most_liquid_by_adv20_are_kept_ties_by_symbol_and_the_rest_are_returned_as_cut()
    {
        var universe = new[] { Share("D", 10), Share("B", 50), Share("C", 50), Share("A", 1), Share("E", 30) };

        var (kept, cut) = OpeningWindows.Subscribed(universe, 3);

        kept.Select(k => k.Bars.Symbol).ShouldBe(["B", "C", "E"]);
        cut.Select(k => k.Bars.Symbol).ShouldBe(["D", "A"]);
    }
}
