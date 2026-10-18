using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Simulation.Walking;

namespace XDrishti.Simulation.Tests;

/// <summary>Criterion 13: the grid arithmetic is exact in decimal, and a high (a low) exactly on a decimal level is a touch where a double would miss it (T3 and T5).</summary>
public sealed class TickGridTests
{
    public static TheoryData<decimal> Ticks => [0.01m, 0.05m, 0.10m];

    [Theory]
    [MemberData(nameof(Ticks))]
    public void A_grid_price_built_in_double_is_returned_unchanged_by_both_roundings(decimal tick)
    {
        for (var i = 1; i <= 10_000; i++)
        {
            TickGrid.TryConvert(i * (double)tick, out var price).ShouldBeTrue();

            TickGrid.Down(price, tick).ShouldBe(price, $"tickDown of {price}");
            TickGrid.Up(price, tick).ShouldBe(price, $"tickUp of {price}");
        }
    }

    [Theory]
    [MemberData(nameof(Ticks))]
    public void An_off_grid_price_rounds_down_to_the_grid_price_at_or_below_and_up_to_the_one_at_or_above(decimal tick)
    {
        for (var i = 1; i <= 10_000; i++)
        {
            TickGrid.TryConvert(i * (double)tick, out var grid).ShouldBeTrue();
            var offGrid = grid + (tick / 3);

            TickGrid.Down(offGrid, tick).ShouldBe(grid, $"tickDown of {offGrid}");
            TickGrid.Up(offGrid, tick).ShouldBe(grid + tick, $"tickUp of {offGrid}");
            (TickGrid.Up(offGrid, tick) - TickGrid.Down(offGrid, tick)).ShouldBe(tick);
        }
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(1e19)]
    [InlineData(-1e19)]
    public void A_price_that_is_not_a_finite_number_of_a_sane_size_does_not_convert(double price)
    {
        TickGrid.TryConvert(price, out _).ShouldBeFalse();
    }

    [Fact]
    public void A_double_price_is_converted_rounded_to_eight_decimals()
    {
        TickGrid.TryConvert(102.30000000000001, out var price).ShouldBeTrue();

        price.ShouldBe(102.30m);
    }

    public static TheoryData<bool> Sides => [true, false];

    [Theory]
    [MemberData(nameof(Sides))]
    public void A_high_or_low_exactly_on_the_decimal_target_is_a_touch_in_all_8000_combinations_and_one_cent_short_is_not(bool buy)
    {
        // Entry 100.00 to 100.95 and stop 95.00 to 99.95 on the 0.05 grid, k in {1.5, 2, 2.5, 3}; a sell mirrors with the stop above the entry by the same distance. In double the target
        // entry + k x risk is above the decimal target in about a third of them (102.30000000000001 for H21), so a minute that touches it exactly would be no touch.
        var touches = 0;
        var misses = 0;
        foreach (var entryTicks in Enumerable.Range(0, 20))
        {
            foreach (var stopTicks in Enumerable.Range(0, 100))
            {
                foreach (var k in new[] { 1.5m, 2m, 2.5m, 3m })
                {
                    var entry = 100.00m + (entryTicks * 0.05m);
                    var distance = entry - (95.00m + (stopTicks * 0.05m));
                    var stop = buy ? entry - distance : entry + distance;
                    var target = buy ? entry + (k * distance) : entry - (k * distance);

                    var onTarget = Walk(buy, entry, stop, (double)k, target);
                    var shortOfTarget = Walk(buy, entry, stop, (double)k, buy ? target - 0.01m : target + 0.01m);

                    onTarget.Trade.ShouldNotBeNull().ExitReason.ShouldBe(ExitReason.Target, $"entry {entry} stop {stop} k {k}");
                    onTarget.Trade.ExitPrice.ShouldBe((double)target, $"entry {entry} stop {stop} k {k}");
                    shortOfTarget.Trade.ShouldNotBeNull().ExitReason.ShouldBe(ExitReason.EndOfData, $"entry {entry} stop {stop} k {k}");
                    touches++;
                    misses++;
                }
            }
        }

        touches.ShouldBe(8_000);
        misses.ShouldBe(8_000);
    }

    [Fact]
    public void In_double_the_target_of_H21_and_H22_is_not_the_decimal_target()
    {
        (100.00 + (2 * Math.Abs(100.00 - 98.85))).ShouldNotBe(102.30);
        (199.85 - (2 * Math.Abs(199.85 - 200.90))).ShouldNotBe(197.75);
    }

    [Fact]
    public void A_low_exactly_on_the_stop_touches_it_and_a_cent_above_does_not()
    {
        // H23: stop 99.05; low 99.05 is a touch (the exit is the stop), 99.06 is not.
        var touch = Hand.Buy("stop-touch", 100.00, 99.05).Rows("10:00 100.00/100.30/99.80/100.20", "10:20 99.60/99.70/99.05/99.30").Walk(FillRules.Default).Trade.ShouldNotBeNull();
        var miss = Hand.Buy("stop-miss", 100.00, 99.05).Rows("10:00 100.00/100.30/99.80/100.20", "10:20 99.60/99.70/99.06/99.30").Walk(FillRules.Default).Trade.ShouldNotBeNull();

        touch.ExitReason.ShouldBe(ExitReason.Stop);
        touch.ExitPrice.ShouldBe(99.05);
        miss.ExitReason.ShouldBe(ExitReason.SquareOff);
    }

    /// <summary>Entry at the open 10:00 (flat), then a minute 10:01 whose high (a sell: low) is the given extreme and whose other extreme stays at the entry; the list ends there.</summary>
    private static WalkOutcome Walk(bool buy, decimal entry, decimal stop, double rewardRisk, decimal extreme)
    {
        var date = Day.Monday;
        var price = (double)entry;
        var bars = new[]
        {
            new Bar(Day.Epoch(date, Day.Minute("09:59")), price, price, price, price, 1_000),
            new Bar(Day.Epoch(date, Day.Minute("10:00")), price, price, price, price, 1_000),
            new Bar(Day.Epoch(date, Day.Minute("10:01")), price, buy ? (double)extreme : price, buy ? price : (double)extreme, price, 1_000),
        };
        var trigger = new Bar(Day.Epoch(date, Day.Minute("09:45")), price, price, price, price, 15_000);
        var candidate = new EntryCandidate("ORB", Cand.Test, buy ? Side.Buy : Side.Sell, 0, Day.Epoch(date, Day.Minute("10:00")), Timeframe.M15, trigger, 15, true, (double)stop, DecisionTrace.Empty);

        var result = TradeWalker.Walk(candidate, bars, new WalkRules(rewardRisk, 915, Cand.NoSlippage, Cand.Tick05));
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code : string.Empty);
        return result.Value;
    }
}
