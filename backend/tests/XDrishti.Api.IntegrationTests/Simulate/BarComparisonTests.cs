using XDrishti.Api.Simulate.Assist;
using XDrishti.SimReader;
using static XDrishti.Api.IntegrationTests.Simulate.AssistSupport;

namespace XDrishti.Api.IntegrationTests.Simulate;

/// <summary>P3-09c criterion 5: the bar comparison reports exactly the planted differences, with the words of D3.</summary>
public sealed class BarComparisonTests
{
    private static readonly string[] Minutes = ["09:15", "09:16", "09:17", "09:18", "09:19"];

    private const decimal FixtureTick = 0.05m;

    private static List<ReaderBar> Five() => [.. BuyDay().Take(5)];

    private static IReadOnlyList<MinuteComparison> Compare(List<ReaderBar> stored, List<ReaderBar> second) =>
        BarComparison.Compare(stored, second, [.. Minutes.Select(Epoch)], FixtureTick);

    [Fact]
    public void Equal_days_match_on_every_minute()
    {
        var result = Compare(Five(), Five());

        result.Count.ShouldBe(5);
        result.ShouldAllBe(m => m.Matches && m.Words == "match");
    }

    [Fact]
    public void A_planted_two_ticks_a_one_tick_noise_a_missing_minute_and_a_widened_opening_bar_are_reported_with_the_right_words()
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
        result["09:17"].ShouldBe("match (1 tick)");
        result["09:18"].ShouldBe("missing in stored");
        result["09:19"].ShouldBe("missing in second source");
    }

    [Fact]
    public void A_difference_of_one_tick_or_less_is_a_match_counted_as_noise()
    {
        var second = Five();
        var stored = Five();
        FixtureTick.ShouldBe(0.05m);
        stored[1] = stored[1] with { Low = stored[1].Low - 0.01m };
        stored[2] = stored[2] with { Open = stored[2].Open + 0.05m };
        stored[3] = stored[3] with { High = stored[3].High + 0.06m };

        var result = Compare(stored, second).ToDictionary(m => m.Minute);

        result["09:16"].Matches.ShouldBeTrue();
        result["09:16"].IsNoise.ShouldBeTrue();
        result["09:17"].Matches.ShouldBeTrue();
        result["09:18"].Matches.ShouldBeFalse();
        result["09:18"].Words.ShouldBe("differs by 2 ticks");
        result["09:18"].Ticks.ShouldBe(2);
    }

    [Fact]
    public void Open_and_close_have_a_looser_tolerance_than_high_and_low()
    {
        var stored = Five();
        stored[0] = stored[0] with { Open = stored[0].Open + 0.10m, Close = stored[0].Close - 0.10m };
        stored[1] = stored[1] with { Open = stored[1].Open + 0.30m };

        var result = Compare(stored, Five()).ToDictionary(m => m.Minute);

        result["09:15"].Matches.ShouldBeTrue();
        result["09:15"].IsNoise.ShouldBeTrue();
        result["09:16"].Matches.ShouldBeFalse();
    }

    private static List<ReaderBar> Level(IEnumerable<ReaderBar> bars, decimal ratio) =>
        [.. bars.Select(b => b with { Open = b.Open * ratio, High = b.High * ratio, Low = b.Low * ratio, Close = b.Close * ratio })];

    [Fact]
    public void A_constant_price_level_offset_is_noted_and_the_minutes_match_after_scaling()
    {
        var outcome = BarComparison.Analyse(Level(Five(), 0.98m), Five(), [.. Minutes.Select(Epoch)], 0.05m);

        outcome.OffsetNote.ShouldNotBeNull().ShouldContain("price level offset: stored is 0.98000 x second source");
        outcome.Minutes.ShouldAllBe(m => m.Matches);
    }

    [Fact]
    public void An_offset_that_changes_inside_the_day_is_not_constant()
    {
        var stored = Level(Five().Take(3), 0.98m);
        stored.AddRange(Five().Skip(3));

        var outcome = BarComparison.Analyse(stored, Five(), [.. Minutes.Select(Epoch)], 0.05m);

        outcome.OffsetNote.ShouldNotBeNull();
        outcome.Minutes.Where(m => !m.Matches).Select(m => m.Words).ShouldAllBe(w => w.StartsWith("price level not constant", StringComparison.Ordinal));
        outcome.Minutes.Count(m => !m.Matches).ShouldBe(2);
    }

    [Fact]
    public void A_ten_tick_open_difference_on_a_price_of_1350_is_still_reported()
    {
        long at = Epoch("09:15");
        var stored = new List<ReaderBar> { new(at, 1350.50m, 1352m, 1349m, 1351m, 1m) };
        var second = new List<ReaderBar> { new(at, 1350m, 1352m, 1349m, 1351m, 1m) };

        var minute = BarComparison.Compare(stored, second, [at], 0.05m).Single();

        minute.Matches.ShouldBeFalse();
        minute.Words.ShouldBe(BarComparison.Iss21 + "; differs by 10 ticks");
    }

    [Theory]
    [InlineData(100)]
    [InlineData(0.01)]
    [InlineData(0)]
    public void A_ratio_outside_the_adjustment_band_is_not_scaled_and_is_reported(double ratio)
    {
        var outcome = BarComparison.Analyse(Level(Five(), (decimal)ratio), Five(), [.. Minutes.Select(Epoch)], 0.05m);

        outcome.OffsetNote.ShouldNotBeNull().ShouldContain("outside the adjustment band");
        outcome.Minutes.ShouldAllBe(m => !m.Matches && m.Words.Contains("differs by", StringComparison.Ordinal));
    }

    [Fact]
    public void Fewer_than_three_paired_minutes_are_not_scaled_and_say_why()
    {
        var outcome = BarComparison.Analyse(Level(Five().Take(2), 0.98m), Five().Take(2).ToList(), [Epoch("09:15"), Epoch("09:16")], 0.05m);

        outcome.OffsetNote.ShouldNotBeNull().ShouldContain("only 2 paired minutes");
        outcome.Minutes.ShouldAllBe(m => !m.Matches);
    }

    [Fact]
    public void A_one_minute_timestamp_shift_on_a_moving_series_is_caught_not_absorbed()
    {
        var start = Epoch("09:15");
        var stored = Enumerable.Range(0, 8).Select(i => i % 2 == 0 ? new ReaderBar(start + (i * 60L), 100m, 100m, 100m, 100m, 1m) : new ReaderBar(start + (i * 60L), 110m, 110m, 110m, 110m, 1m)).ToList();
        var shifted = stored.Select(b => b with { EpochSeconds = b.EpochSeconds + 60 }).ToList();

        var outcome = BarComparison.Analyse(stored, shifted, [.. stored.Select(b => b.EpochSeconds)], 0.05m);

        outcome.Minutes.Count(m => !m.Matches).ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Volume_is_not_compared()
    {
        var stored = Five();
        stored[0] = stored[0] with { Volume = stored[0].Volume + 5000 };

        Compare(stored, Five()).ShouldAllBe(m => m.Words == "match");
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
