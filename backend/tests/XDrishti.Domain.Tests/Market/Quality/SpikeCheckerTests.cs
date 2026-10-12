using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Quality;
using static XDrishti.Domain.Tests.Market.Quality.QualityFixtures;

namespace XDrishti.Domain.Tests.Market.Quality;

public sealed class SpikeCheckerTests
{
    private static readonly int Open = Minute(9, 15);

    /// <summary>Calm minutes at 100, with the closes of chosen minutes replaced.</summary>
    private static List<Bar> DayWith(DateOnly date, params (int Minute, double Close)[] closes)
    {
        var replaced = closes.ToDictionary(c => c.Minute, c => c.Close);
        return [.. Enumerable.Range(Open, 60).Select(m => replaced.TryGetValue(m, out var close) ? Of(date, m, close, close, close, close) : Of(date, m, 100, 100.02, 99.99, 100.01))];
    }

    [Fact]
    public void A_calm_day_has_no_spike()
    {
        var check = SpikeChecker.Check(DayWith(Wed), 20);

        (check.Status, check.Spikes, check.LargeMoves).ShouldBe((CheckStatus.Pass, 0, 0));
        check.ThresholdPercent.ShouldBe(1);
    }

    [Fact]
    public void A_jump_that_the_next_bar_takes_back_is_a_spike_and_a_warning_and_its_return_is_not_counted_again()
    {
        var bars = DayWith(Wed, (Open + 30, 105), (Open + 31, 100.01));

        var check = SpikeChecker.Check(bars, 20);

        (check.Status, check.Spikes, check.LargeMoves).ShouldBe((CheckStatus.Warning, 1, 0));
        var example = check.Examples.ShouldHaveSingleItem();
        example.Reversed.ShouldBeTrue();
        example.MovePercent.ShouldBe(4.99); // from the previous close, 100.01
        example.Ts.ShouldBe(new DateTimeOffset(2026, 10, 7, 9, 45, 0, TimeSpan.FromMinutes(330)));
    }

    [Fact]
    public void A_jump_that_stays_is_a_real_move_reported_but_not_a_warning()
    {
        var bars = DayWith(Wed, (Open + 30, 105))
            .Select(b => b.EpochSeconds >= Epoch(Wed, Open + 31) ? b with { Open = 105, High = 105.02, Low = 104.99, Close = 105.01 } : b)
            .ToList();

        var check = SpikeChecker.Check(bars, 20);

        (check.Status, check.Spikes, check.LargeMoves).ShouldBe((CheckStatus.Pass, 0, 1));
        check.Examples.ShouldHaveSingleItem().Reversed.ShouldBeFalse();
    }

    [Fact]
    public void A_jump_that_is_only_partly_taken_back_is_a_real_move()
    {
        var bars = DayWith(Wed, (Open + 30, 105), (Open + 31, 103), (Open + 32, 103));

        var check = SpikeChecker.Check(bars, 20);

        check.Spikes.ShouldBe(0);
        check.LargeMoves.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void A_move_under_one_percent_is_never_a_spike_however_calm_the_instrument_is()
    {
        var bars = DayWith(Wed, (Open + 30, 100.9), (Open + 31, 100.01));

        SpikeChecker.Check(bars, 20).Spikes.ShouldBe(0);
    }

    [Fact]
    public void The_median_move_raises_the_bar_for_a_restless_instrument()
    {
        // Every minute moves 0.5 %: the median is 0.5 %, so with k = 20 a 5 % move is not enough, with k = 5 it is.
        var restless = Enumerable.Range(0, 60).Select(i =>
        {
            var close = i % 2 == 0 ? 100.0 : 100.5;
            return Of(Wed, Open + i, close, close, close, close);
        }).ToList();
        restless[30] = Of(Wed, Open + 30, 105, 105, 105, 105);
        restless[31] = Of(Wed, Open + 31, 100, 100, 100, 100);

        SpikeChecker.Check(restless, 20).Spikes.ShouldBe(0);
        SpikeChecker.Check(restless, 5).Spikes.ShouldBe(1);
    }

    [Fact]
    public void The_gap_between_two_sessions_is_not_a_move()
    {
        var bars = DayWith(Tue).Concat(DayWith(Wed).Select(b => b with { Open = 120, High = 120.02, Low = 119.99, Close = 120.01 })).ToList();

        var check = SpikeChecker.Check(bars, 20);

        (check.Spikes, check.LargeMoves).ShouldBe((0, 0));
    }

    [Fact]
    public void A_spike_on_the_last_bar_of_a_session_cannot_be_judged_and_is_a_large_move()
    {
        var bars = DayWith(Wed, (Open + 59, 108));

        var check = SpikeChecker.Check(bars, 20);

        (check.Spikes, check.LargeMoves).ShouldBe((0, 1));
    }

    [Fact]
    public void A_downward_spike_has_a_negative_move()
    {
        var bars = DayWith(Wed, (Open + 10, 95), (Open + 11, 100));

        SpikeChecker.Check(bars, 20).Examples.Single().MovePercent.ShouldBe(-5.01);
    }

    [Fact]
    public void No_bars_pass()
    {
        SpikeChecker.Check([], 20).Status.ShouldBe(CheckStatus.Pass);
    }
}
