using XDrishti.Api.Simulate.Assist;
using XDrishti.SimReader;
using static XDrishti.Api.IntegrationTests.Simulate.AssistSupport;

namespace XDrishti.Api.IntegrationTests.Simulate;

/// <summary>P3-09c criterion 5: the bar comparison reports exactly the planted differences, with the words of D3.</summary>
public sealed class BarComparisonTests
{
    private static readonly string[] Minutes = ["09:15", "09:16", "09:17", "09:18", "09:19"];

    private static List<ReaderBar> Five() => [.. BuyDay().Take(5)];

    private static IReadOnlyList<MinuteComparison> Compare(List<ReaderBar> stored, List<ReaderBar> second) =>
        BarComparison.Compare(stored, second, [.. Minutes.Select(Epoch)], 0.05m);

    [Fact]
    public void Equal_days_match_on_every_minute()
    {
        var result = Compare(Five(), Five());

        result.Count.ShouldBe(5);
        result.ShouldAllBe(m => m.Matches && m.Words == "match");
    }

    [Fact]
    public void A_planted_tick_a_missing_minute_and_a_widened_opening_bar_are_reported_with_the_right_words()
    {
        var stored = Five();
        var second = Five();
        stored[0] = stored[0] with { High = stored[0].High + 0.10m };
        stored[2] = stored[2] with { Close = stored[2].Close + 0.05m };
        stored.RemoveAt(3);
        second.RemoveAt(4);

        var result = Compare(stored, second).ToDictionary(m => m.Minute, m => m.Words);

        result["09:15"].ShouldBe(BarComparison.Iss21 + "; differs by 2 ticks");
        result["09:16"].ShouldBe("match");
        result["09:17"].ShouldBe("differs by 1 tick");
        result["09:18"].ShouldBe("missing in stored");
        result["09:19"].ShouldBe("missing in second source");
    }

    [Fact]
    public void A_difference_below_a_tick_still_counts_as_a_tick()
    {
        var second = Five();
        var stored = Five();
        stored[1] = stored[1] with { Low = stored[1].Low - 0.01m };

        Compare(stored, second).Single(m => m.Minute == "09:16").Words.ShouldBe("differs by 1 tick");
    }

    [Fact]
    public void A_minute_neither_side_has_is_not_reported()
    {
        BarComparison.Compare([], [], [Epoch("09:15")], 0.05m).ShouldBeEmpty();
    }

    [Fact]
    public void The_minutes_that_matter_are_the_range_the_trigger_bar_the_entry_and_the_exit_with_their_neighbours()
    {
        var reading = Read(BuyDay());

        var minutes = BarComparison.Minutes(Epoch("09:15"), ReaderStrategy.OrbV1(0.05m), reading).Select(BarComparison.Label).ToList();

        minutes.ShouldContain("09:15");
        minutes.ShouldContain("09:29");
        minutes.ShouldContain("09:44");
        minutes.ShouldContain("09:45");
        minutes.ShouldContain("09:46");
        minutes.ShouldContain("15:14");
        minutes.ShouldContain("15:16");
        minutes.ShouldNotContain("12:00");
    }
}
