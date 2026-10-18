using XDrishti.Domain.Research;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Simulation.Walking;

namespace XDrishti.Simulation.Tests;

/// <summary>
/// Criterion 17: with the entry limit of 0.1 % and no slippage, P3-03's fixtures and walker rows give P3-03's tables (their expectations are asserted field by field in <see cref="TradeWalkerFixtureTests"/> and
/// <see cref="TradeWalkerRuleTests"/>, which run on <see cref="Cand.Standard"/>); W13 and W14 are the only rows that differ, and this class pins both numbers of each difference. Nothing else in a P3-03 table moved.
/// </summary>
public sealed class CompatibilityWithP303Tests
{
    [Fact]
    public void In_every_fixture_and_row_the_limit_does_not_bind_so_the_entry_is_the_open_of_the_fill_minute()
    {
        var trades = 0;
        foreach (var walkCase in Cases.All)
        {
            foreach (var item in walkCase.Items)
            {
                var outcome = WalkAssert.Walk(walkCase, item).Value;

                outcome.NoTrade.ShouldNotBe(NoTradeReason.LimitNotReached, walkCase.Name);
                if (outcome.Trade is { } trade)
                {
                    trade.EntryFill.ShouldBe(EntryFill.AtOpen, walkCase.Name);
                    trades++;
                }
            }
        }

        trades.ShouldBeGreaterThanOrEqualTo(25);
    }

    [Fact]
    public void W13_the_first_print_beyond_the_target_decides_for_the_target_where_P3_03_took_the_stop()
    {
        var w13 = Cases.W("W13");

        var outcome = WalkAssert.Walk(w13, w13.Items[0]).Value;

        var trade = outcome.Trade.ShouldNotBeNull();
        trade.ExitReason.ShouldBe(ExitReason.Target);
        trade.ExitPrice.ShouldBe(105.50);
        trade.RExact.ShouldBe(2.0);
        trade.MfeR.ShouldBe(2.0);
        outcome.TargetGapExit.ShouldBeTrue();
        outcome.AmbiguousMinute.ShouldBeFalse("the open is beyond the target: the first print decides, this is not the ambiguity rule");

        // P3-03: ExitReason.Stop at 98.00, R -1.0, MAE 1.0, MFE 0.0, no gap counter.
        (trade.ExitReason, trade.ExitPrice, trade.RExact).ShouldNotBe((ExitReason.Stop, 98.00, -1.0));
    }

    [Fact]
    public void W14_the_square_off_minute_that_opens_beyond_a_level_fills_that_level_where_P3_03_exited_at_the_open_as_a_square_off()
    {
        var stopSide = Cases.W("W14-opens-beyond-the-stop");
        var targetSide = Cases.W("W14-opens-beyond-the-target");

        var stop = WalkAssert.Walk(stopSide, stopSide.Items[0]).Value;
        var target = WalkAssert.Walk(targetSide, targetSide.Items[0]).Value;

        stop.Trade.ShouldNotBeNull().ExitReason.ShouldBe(ExitReason.Stop);
        stop.Trade.ExitPrice.ShouldBe(97.50);
        stop.Trade.RExact.ShouldBe(-1.2);
        stop.Trade.MaeR.ShouldBe(1.2);
        stop.StopGapExit.ShouldBeTrue();
        target.Trade.ShouldNotBeNull().ExitReason.ShouldBe(ExitReason.Target);
        target.Trade.ExitPrice.ShouldBe(105.50);
        target.Trade.RExact.ShouldBe(2.0);
        target.TargetGapExit.ShouldBeTrue();

        // P3-03: SquareOff at the open in both: 97.50 (R -1.2, the same R, no counter) and 106.00 (R +2.2).
    }

    [Fact]
    public void Run_with_the_doc_16_slippage_the_same_candidates_make_the_same_decisions_and_the_filled_layer_is_never_better()
    {
        foreach (var walkCase in Cases.All)
        {
            foreach (var item in walkCase.Items)
            {
                var exact = WalkAssert.Walk(walkCase, item).Value;
                var modelled = TradeWalker.Walk(item.Candidate, walkCase.Bars, walkCase.Rules with { Fills = FillRules.Default }).Value;

                modelled.NoTrade.ShouldBe(exact.NoTrade, walkCase.Name);
                modelled.BarsExamined.ShouldBe(exact.BarsExamined, walkCase.Name);
                if (exact.Trade is { } e)
                {
                    var m = modelled.Trade.ShouldNotBeNull(walkCase.Name);
                    (m.ExitReason, m.EntryEpochSeconds, m.ExitEpochSeconds, m.EntryPrice, m.ExitPrice, m.RExact, m.MaeR, m.MfeR)
                        .ShouldBe((e.ExitReason, e.EntryEpochSeconds, e.ExitEpochSeconds, e.EntryPrice, e.ExitPrice, e.RExact, e.MaeR, e.MfeR), walkCase.Name);
                    m.RFilled.ShouldBeLessThanOrEqualTo(m.RExact, walkCase.Name);
                    m.Fills.ShouldBe(FillBasis.Modelled, walkCase.Name);
                }
            }
        }
    }
}
