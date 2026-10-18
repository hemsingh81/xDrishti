using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Tests.Support;

namespace XDrishti.Simulation.Tests;

/// <summary>AC9: over the seeded runs of AC8, every trade fills where D3 says and its R, MAE and MFE equal a plain reference loop written in this test.</summary>
public sealed class FillTimingPropertyTests
{
    private static readonly List<SeededRun> Runs = [.. Enumerable.Range(1, 100).Select(SeededRuns.Make)];

    [Fact]
    public void Every_trade_fills_in_the_minute_after_the_candidate_within_the_delay_and_before_the_square_off()
    {
        var checkedTrades = 0;
        foreach (var run in Runs)
        {
            foreach (var (walk, outcome, _) in LookAhead.FullRun(run, LookAhead.Identity).Where(o => o.Outcome.Trade is not null))
            {
                var trade = outcome.Trade!;
                var candidate = walk.Candidate;
                var own = run.Bars[candidate.DecisionBarIndex];
                var fill = run.Bars[candidate.DecisionBarIndex + 1];

                trade.EntryEpochSeconds.ShouldBe(fill.EpochSeconds);
                SessionClock.DateOf(fill.EpochSeconds).ShouldBe(SessionClock.DateOf(own.EpochSeconds));
                (fill.EpochSeconds - candidate.DecisionEpochSeconds).ShouldBeInRange(0, SimulationEngine.MaxFillDelayMinutes * 60L);
                SessionClock.MinuteOfDay(fill.EpochSeconds).ShouldBeLessThan(walk.Rules.SquareOffMinute);
                trade.EntryPrice.ShouldBe(fill.Open);
                trade.TradeDate.ShouldBe(SessionClock.DateOf(fill.EpochSeconds));
                outcome.EntryAfterMissingMinutes.ShouldBe(fill.EpochSeconds > candidate.DecisionEpochSeconds);
                checkedTrades++;
            }
        }

        checkedTrades.ShouldBeGreaterThanOrEqualTo(100);
    }

    [Fact]
    public void Every_exit_price_lies_in_its_minute_or_is_the_open_or_the_close_the_rule_names()
    {
        foreach (var run in Runs)
        {
            foreach (var (walk, outcome, settled) in LookAhead.FullRun(run, LookAhead.Identity).Where(o => o.Outcome.Trade is not null))
            {
                var trade = outcome.Trade!;
                var exit = run.Bars[settled];
                var fillIndex = walk.Candidate.DecisionBarIndex + 1;

                settled.ShouldBeGreaterThanOrEqualTo(fillIndex);
                SessionClock.DateOf(exit.EpochSeconds).ShouldBe(trade.TradeDate);
                var inside = trade.ExitPrice >= exit.Low && trade.ExitPrice <= exit.High;
                var openOrClose = trade.ExitPrice == exit.Open || trade.ExitPrice == exit.Close;
                (inside || openOrClose || outcome.TargetGapExit).ShouldBeTrue($"seed {run.Seed}: {trade.ExitReason} at {trade.ExitPrice} in {exit}");
                if (trade.ExitReason == ExitReason.SquareOff)
                {
                    trade.ExitPrice.ShouldBe(exit.Open);
                    SessionClock.MinuteOfDay(exit.EpochSeconds).ShouldBeGreaterThanOrEqualTo(walk.Rules.SquareOffMinute);
                }

                if (trade.ExitReason == ExitReason.EndOfData)
                {
                    trade.ExitPrice.ShouldBe(exit.Close);
                }
            }
        }
    }

    [Fact]
    public void R_and_the_excursions_equal_a_plain_reference_loop_and_the_exit_is_inside_them()
    {
        foreach (var run in Runs)
        {
            foreach (var (walk, outcome, settled) in LookAhead.FullRun(run, LookAhead.Identity).Where(o => o.Outcome.Trade is not null))
            {
                var trade = outcome.Trade!;
                var (r, mae, mfe) = Reference(trade, run.Bars, walk.Candidate.DecisionBarIndex + 1, settled);

                trade.R.ShouldBe(r, $"seed {run.Seed} R");
                trade.MaeR.ShouldBe(mae, $"seed {run.Seed} MAE");
                trade.MfeR.ShouldBe(mfe, $"seed {run.Seed} MFE");
                trade.MaeR.ShouldBeGreaterThanOrEqualTo(0);
                trade.MfeR.ShouldBeGreaterThanOrEqualTo(0);
                (-trade.MaeR).ShouldBeLessThanOrEqualTo(trade.R);
                trade.R.ShouldBeLessThanOrEqualTo(trade.MfeR);
                outcome.BarsExamined.ShouldBe(settled - (walk.Candidate.DecisionBarIndex + 1) + 1);
            }
        }
    }

    /// <summary>
    /// The definition of D5 in the signed form: a move of a price is <c>s · (p − entry) / risk</c> with <c>s</c> = +1 for a buy and −1 for a sell; MFE is the best move seen and MAE the worst, never beyond 0; the exit
    /// minute contributes only what D5 allows (stop: the exit price; target: its adverse extreme and the target; square-off: its open; end of data: everything).
    /// </summary>
    private static (double R, double Mae, double Mfe) Reference(SimTrade trade, IReadOnlyList<Bar> bars, int fillIndex, int exitIndex)
    {
        var sign = trade.Side == Side.Buy ? 1.0 : -1.0;
        var risk = Math.Abs(trade.EntryPrice - trade.StopPrice);
        double Move(double price) => sign * (price - trade.EntryPrice) / risk;
        var best = 0.0;
        var worst = 0.0;
        void See(double price)
        {
            best = Math.Max(best, Move(price));
            worst = Math.Min(worst, Move(price));
        }

        for (var i = fillIndex; i <= exitIndex; i++)
        {
            var bar = bars[i];
            if (i < exitIndex || trade.ExitReason == ExitReason.EndOfData)
            {
                See(bar.Low);
                See(bar.High);
                continue;
            }

            switch (trade.ExitReason)
            {
                case ExitReason.Stop:
                    See(trade.ExitPrice);
                    break;
                case ExitReason.Target:
                    // only the adverse side of the minute counts, and only where it is below the entry (a minute that gapped beyond the target is wholly favourable)
                    worst = Math.Min(worst, Move(trade.Side == Side.Buy ? bar.Low : bar.High));
                    See(trade.ExitPrice);
                    break;
                case ExitReason.SquareOff:
                    See(bar.Open);
                    break;
            }
        }

        return (Move(trade.ExitPrice), -worst, best);
    }
}
