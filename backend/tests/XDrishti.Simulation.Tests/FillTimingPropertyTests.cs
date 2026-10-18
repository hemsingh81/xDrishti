using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Tests.Support;

namespace XDrishti.Simulation.Tests;

/// <summary>
/// Criterion 19 (P3-03's AC9 extended to the rule table): over the 500 seeded days, every trade fills where E1 says and leaves where X1 to X6 say; every exact fill price lies in its minute or is its order's own level; in modelled mode
/// every fill price is on the grid and within the slippage of the exact one; and the excursions bracket the result.
/// </summary>
public sealed class FillTimingPropertyTests
{
    private const double Tolerance = 1e-9;

    private static readonly List<DayWalk> Exact = WalkProperties.WalkAll(SeededDays.All, FillRules.Create(0.1, SlippageConfig.None).Value);

    private static readonly List<DayWalk> Modelled = WalkProperties.WalkAll(SeededDays.All, FillRules.Default);

    [Fact]
    public void Every_fill_minute_holds_a_trade_starts_at_or_after_the_decision_at_most_five_minutes_after_it_and_before_the_square_off()
    {
        var checkedTrades = 0;
        foreach (var walk in Exact.Where(w => w.Trade is not null))
        {
            var trade = walk.Trade!;
            var bars = walk.Day.Bars;
            var candidate = walk.Walk.Candidate;
            var fill = bars[WalkProperties.IndexOf(bars, trade.EntryEpochSeconds, candidate.DecisionBarIndex + 1)];

            fill.Volume.ShouldBeGreaterThan(0, $"seed {walk.Day.Seed}");
            (fill.EpochSeconds - candidate.DecisionEpochSeconds).ShouldBeInRange(0, SimulationEngine.MaxFillDelayMinutes * 60L);
            SessionClock.DateOf(fill.EpochSeconds).ShouldBe(SessionClock.DateOf(bars[candidate.DecisionBarIndex].EpochSeconds));
            SessionClock.MinuteOfDay(fill.EpochSeconds).ShouldBeLessThan(walk.Day.SquareOffMinute);
            trade.TradeDate.ShouldBe(SessionClock.DateOf(fill.EpochSeconds));
            walk.Outcome.EntryAfterMissingMinutes.ShouldBe(fill.EpochSeconds > candidate.DecisionEpochSeconds);

            // It is the first minute that holds a trade after the candidate's own: nothing between holds one.
            bars.Skip(candidate.DecisionBarIndex + 1).TakeWhile(b => b.EpochSeconds < fill.EpochSeconds).ShouldAllBe(b => b.Volume == 0, $"seed {walk.Day.Seed}");
            checkedTrades++;
        }

        checkedTrades.ShouldBeGreaterThanOrEqualTo(1_500);
    }

    [Fact]
    public void Every_exit_minute_is_on_the_entry_date_holds_a_trade_and_is_not_before_the_fill_minute()
    {
        foreach (var walk in Exact.Where(w => w.Trade is not null))
        {
            var trade = walk.Trade!;
            var bars = walk.Day.Bars;
            var exit = bars[WalkProperties.IndexOf(bars, trade.ExitEpochSeconds, walk.Walk.Candidate.DecisionBarIndex + 1)];

            SessionClock.DateOf(exit.EpochSeconds).ShouldBe(trade.TradeDate, $"seed {walk.Day.Seed}");
            exit.Volume.ShouldBeGreaterThan(0, $"seed {walk.Day.Seed}");
            trade.ExitEpochSeconds.ShouldBeGreaterThanOrEqualTo(trade.EntryEpochSeconds);
        }
    }

    [Fact]
    public void A_square_off_is_at_the_first_minute_that_holds_a_trade_at_or_after_the_square_off_by_the_tests_own_scan()
    {
        var squareOffs = 0;
        foreach (var walk in Exact.Where(w => w.Trade is not null))
        {
            var trade = walk.Trade!;
            var bars = walk.Day.Bars;
            var firstAfter = bars.FirstOrDefault(b => SessionClock.DateOf(b.EpochSeconds) == trade.TradeDate && SessionClock.MinuteOfDay(b.EpochSeconds) >= walk.Day.SquareOffMinute && b.Volume > 0);
            var exitMinute = SessionClock.MinuteOfDay(trade.ExitEpochSeconds);

            if (trade.ExitReason == ExitReason.SquareOff)
            {
                trade.ExitEpochSeconds.ShouldBe(firstAfter.EpochSeconds, $"seed {walk.Day.Seed}");
                trade.ExitPrice.ShouldBe(firstAfter.Open, $"seed {walk.Day.Seed}");
                squareOffs++;
            }
            else if (trade.ExitReason == ExitReason.EndOfData)
            {
                firstAfter.ShouldBe(default, $"seed {walk.Day.Seed}: no minute at or after the square-off holds a trade");
            }
            else
            {
                // A stop or target before the square-off, or at the square-off minute when its open is beyond a level (X1).
                (exitMinute < walk.Day.SquareOffMinute || trade.ExitEpochSeconds == firstAfter.EpochSeconds).ShouldBeTrue($"seed {walk.Day.Seed}");
            }
        }

        squareOffs.ShouldBeGreaterThanOrEqualTo(100);
    }

    [Fact]
    public void Every_exact_fill_price_lies_in_its_minute_or_is_the_orders_own_level()
    {
        foreach (var walk in Exact.Where(w => w.Trade is not null))
        {
            var trade = walk.Trade!;
            var bars = walk.Day.Bars;
            var fill = bars[WalkProperties.IndexOf(bars, trade.EntryEpochSeconds, walk.Walk.Candidate.DecisionBarIndex + 1)];
            var exit = bars[WalkProperties.IndexOf(bars, trade.ExitEpochSeconds, walk.Walk.Candidate.DecisionBarIndex + 1)];
            var label = $"seed {walk.Day.Seed} {trade.Side} {trade.ExitReason}";

            (trade.EntryPrice >= fill.Low - Tolerance && trade.EntryPrice <= fill.High + Tolerance).ShouldBeTrue(label + " entry " + trade.EntryPrice + " in " + fill);
            if (trade.EntryFill == EntryFill.AtOpen)
            {
                trade.EntryPrice.ShouldBe(fill.Open, Tolerance, label);
            }

            var gappedTarget = trade.ExitReason == ExitReason.Target && walk.Outcome.TargetGapExit;
            ((trade.ExitPrice >= exit.Low - Tolerance && trade.ExitPrice <= exit.High + Tolerance) || gappedTarget).ShouldBeTrue(label + " exit " + trade.ExitPrice + " in " + exit);
            if (trade.ExitReason == ExitReason.EndOfData)
            {
                trade.ExitPrice.ShouldBe(exit.Close, Tolerance, label);
            }

            if (trade.ExitReason == ExitReason.Target)
            {
                trade.ExitPrice.ShouldBe(trade.TargetPrice!.Value, Tolerance, label);
                trade.RExact.ShouldBe(Math.Abs(trade.TargetPrice.Value - trade.EntryPrice) / Math.Abs(trade.EntryPrice - trade.StopPrice), Tolerance, label);
            }

            if (trade.ExitReason == ExitReason.Stop)
            {
                trade.RExact.ShouldBeLessThanOrEqualTo(-1 + Tolerance, label);
                if (!walk.Outcome.StopGapExit)
                {
                    trade.ExitPrice.ShouldBe(trade.StopPrice, Tolerance, label);
                    trade.RExact.ShouldBe(-1, Tolerance, label);
                }
            }
        }
    }

    [Fact]
    public void In_modelled_mode_every_fill_price_is_on_the_grid_and_within_the_slippage_of_the_exact_one()
    {
        var slippage = SlippageConfig.Default;
        foreach (var walk in Modelled.Where(w => w.Trade is not null))
        {
            var trade = walk.Trade!;
            var bars = walk.Day.Bars;
            var tick = walk.Day.Tick;
            var fill = bars[WalkProperties.IndexOf(bars, trade.EntryEpochSeconds, walk.Walk.Candidate.DecisionBarIndex + 1)];
            var exit = bars[WalkProperties.IndexOf(bars, trade.ExitEpochSeconds, walk.Walk.Candidate.DecisionBarIndex + 1)];
            var label = $"seed {walk.Day.Seed} {trade.Side} {trade.ExitReason} {trade.EntryFill}";

            var entryFilled = Round((decimal)trade.EntryFilledPrice);
            var exitFilled = Round((decimal)trade.ExitFilledPrice);
            (entryFilled % tick).ShouldBe(0m, label + " entry on the grid " + entryFilled);
            (exitFilled % tick).ShouldBe(0m, label + " exit on the grid " + exitFilled);

            var entryAmount = trade.EntryFill == EntryFill.AtLimit ? 0m : Amount(slippage, tick, fill, stopExit: false);
            var exitAmount = trade.ExitReason == ExitReason.Target ? 0m : Amount(slippage, tick, exit, stopExit: trade.ExitReason == ExitReason.Stop);
            Math.Abs(entryFilled - Round((decimal)trade.EntryPrice)).ShouldBeLessThanOrEqualTo(entryAmount + tick, label + " entry");
            Math.Abs(exitFilled - Round((decimal)trade.ExitPrice)).ShouldBeLessThanOrEqualTo(exitAmount + tick, label + " exit");
            if (trade.EntryFill == EntryFill.AtLimit)
            {
                trade.EntryFilledPrice.ShouldBe(trade.EntryPrice, Tolerance, label + " a resting limit does not slip");
            }
        }
    }

    [Fact]
    public void Risk_is_above_zero_and_the_excursions_bracket_the_result_in_every_trade()
    {
        foreach (var walk in Exact.Where(w => w.Trade is not null))
        {
            var trade = walk.Trade!;
            var label = $"seed {walk.Day.Seed} {trade.Side} {trade.ExitReason}";

            (trade.Side == Side.Buy ? trade.EntryPrice > trade.StopPrice : trade.EntryPrice < trade.StopPrice).ShouldBeTrue(label + " risk above zero");
            trade.MaeR.ShouldBeGreaterThanOrEqualTo(0, label);
            trade.MfeR.ShouldBeGreaterThanOrEqualTo(0, label);
            (-trade.MaeR).ShouldBeLessThanOrEqualTo(trade.RExact + Tolerance, label);
            trade.RExact.ShouldBeLessThanOrEqualTo(trade.MfeR + Tolerance, label);
        }
    }

    [Fact]
    public void The_excursions_lie_between_what_the_minutes_before_the_exit_prove_and_what_the_whole_range_could_give()
    {
        // Lower bound: the extremes of the minutes that hold a trade between the fill minute (its far side only after an entry at the limit) and the minute before the exit, plus the exit's own distance. Upper bound: the same with
        // the exit minute's whole range. The walker's MAE and MFE must lie between the two.
        foreach (var walk in Exact.Where(w => w.Trade is not null))
        {
            var trade = walk.Trade!;
            var bars = walk.Day.Bars;
            var first = WalkProperties.IndexOf(bars, trade.EntryEpochSeconds, walk.Walk.Candidate.DecisionBarIndex + 1);
            var exitIndex = WalkProperties.IndexOf(bars, trade.ExitEpochSeconds, first);
            var risk = Math.Abs(trade.EntryPrice - trade.StopPrice);
            var buy = trade.Side == Side.Buy;
            var label = $"seed {walk.Day.Seed} {trade.Side} {trade.ExitReason}";

            double Adverse(double low, double high) => Math.Max(0, buy ? (trade.EntryPrice - low) / risk : (high - trade.EntryPrice) / risk);
            double Favourable(double low, double high) => Math.Max(0, buy ? (high - trade.EntryPrice) / risk : (trade.EntryPrice - low) / risk);

            double maeLower = 0, mfeLower = 0;
            for (var i = first; i < exitIndex; i++)
            {
                var bar = bars[i];
                if (bar.Volume == 0)
                {
                    continue;
                }

                var atLimitFill = i == first && trade.EntryFill == EntryFill.AtLimit;
                maeLower = Math.Max(maeLower, Adverse(bar.Low, bar.High));
                mfeLower = Math.Max(mfeLower, atLimitFill ? 0 : Favourable(bar.Low, bar.High));
            }

            var exit = bars[exitIndex];
            var exitDistance = Math.Abs(trade.ExitPrice - trade.EntryPrice) / risk;
            var atLimitAndExitIsFill = exitIndex == first && trade.EntryFill == EntryFill.AtLimit;
            var maeUpper = Math.Max(maeLower, Adverse(exit.Low, exit.High));
            var mfeUpper = atLimitAndExitIsFill ? mfeLower : Math.Max(mfeLower, Favourable(exit.Low, exit.High));

            trade.MaeR.ShouldBeGreaterThanOrEqualTo(maeLower - Tolerance, label + " MAE lower");
            trade.MaeR.ShouldBeLessThanOrEqualTo(Math.Max(maeUpper, exitDistance) + Tolerance, label + " MAE upper");
            trade.MfeR.ShouldBeGreaterThanOrEqualTo(mfeLower - Tolerance, label + " MFE lower");
            trade.MfeR.ShouldBeLessThanOrEqualTo(Math.Max(mfeUpper, trade.ExitReason == ExitReason.Target ? Math.Abs(trade.TargetPrice!.Value - trade.EntryPrice) / risk : 0) + Tolerance, label + " MFE upper");
        }
    }

    private static decimal Round(decimal value) => Math.Round(value, 8);

    private static decimal Amount(SlippageConfig slippage, decimal tick, Bar minute, bool stopExit) =>
        (((decimal)slippage.Ticks + (stopExit ? (decimal)slippage.StopExitExtraTicks : 0m)) * tick) + ((decimal)slippage.BarRangeFrac * Round((decimal)minute.High - (decimal)minute.Low));
}
