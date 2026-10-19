using XDrishti.Domain.Research;
using XDrishti.Simulation.Running;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Strategies.BuiltIn;

namespace XDrishti.Simulation.Tests.Strategies;

/// <summary>
/// Criterion 13 of P3-06, the window independence over 100 generated series: a trade of the run to the end of the data whose decision date is on or before an earlier end <c>To1</c> is, in the run to <c>To1</c>, the same trade (when it left by <c>To1</c>), an
/// open trade at the end (<c>EndOfData</c>, when it had not) or a pending order (when it was decided on the last session); a no-trade candidate is the same no-trade; and an earlier <c>From</c> changes no trade of a later one.
/// </summary>
public sealed class BaseBreakoutPrefixTests
{
    private const int Count = 100;

    private static readonly BaseSeries[] All = [.. Enumerable.Range(1, Count).Select(SeededBase.Make)];

    private static SimulationResult RunUpTo(BaseSeries series, int sessions) =>
        Sim.Run(
            BaseBreakoutV1.Create(series.BaseLen, (double)series.BasePct, (double)series.VolX, (double)series.RewardRisk),
            sessions == series.Dates.Count ? series.Bars() : series.Prefix(sessions),
            XDrishti.Domain.Market.Bars.Timeframe.D1,
            calendar: series.Calendar,
            fills: Cand.NoSlippage);

    private static DateOnly DecisionDate(CandidateOutcome o) => XDrishti.Domain.Market.Bars.SessionClock.DateOf(o.Candidate.DecisionEpochSeconds - 60);

    [Fact]
    public void A_shorter_window_gives_the_same_trade_an_open_trade_a_pending_order_or_the_same_no_trade()
    {
        var compared = 0;
        var kinds = new HashSet<string>();
        foreach (var series in All)
        {
            var cut = 60 + (series.Seed * 7 % (series.Dates.Count - 66));
            var longRun = RunUpTo(series, series.Dates.Count);
            var shortRun = RunUpTo(series, cut);
            var lastDate = series.Dates[cut - 1];

            var expected = longRun.Outcomes.Where(o => DecisionDate(o) <= lastDate).ToList();
            shortRun.Outcomes.Count.ShouldBe(expected.Count, "seed " + series.Seed);
            for (var i = 0; i < expected.Count; i++)
            {
                var (l, s) = (expected[i], shortRun.Outcomes[i]);
                Render.Candidate(s.Candidate).ShouldBe(Render.Candidate(l.Candidate), "seed " + series.Seed);
                var decidedOnTheLastSession = DecisionDate(l) == lastDate;
                if (l.Walk.Trade is { } trade)
                {
                    if (SessionDate(trade.ExitEpochSeconds) <= lastDate && trade.ExitReason != ExitReason.EndOfData)
                    {
                        s.Walk.Trade.ShouldBe(trade, "seed " + series.Seed);
                        kinds.Add("same trade");
                    }
                    else if (decidedOnTheLastSession)
                    {
                        s.Walk.NoTrade.ShouldBe(NoTradeReason.OrderPendingAtEnd, "seed " + series.Seed);
                        kinds.Add("pending");
                    }
                    else
                    {
                        var open = s.Walk.Trade.ShouldNotBeNull("seed " + series.Seed);
                        (open.ExitReason, open.EntryEpochSeconds, open.EntryPrice, open.StopPrice, open.TargetPrice).ShouldBe((ExitReason.EndOfData, trade.EntryEpochSeconds, trade.EntryPrice, trade.StopPrice, trade.TargetPrice), "seed " + series.Seed);
                        kinds.Add("open at end");
                    }
                }
                else
                {
                    var reason = l.Walk.NoTrade;
                    var shortReason = s.Walk.NoTrade;
                    if (decidedOnTheLastSession && reason != NoTradeReason.PositionOpen)
                    {
                        shortReason.ShouldBe(NoTradeReason.OrderPendingAtEnd, "seed " + series.Seed);
                    }
                    else
                    {
                        shortReason.ShouldBe(reason, "seed " + series.Seed);
                    }

                    kinds.Add("no trade " + reason);
                }

                compared++;
            }
        }

        compared.ShouldBeGreaterThanOrEqualTo(40, "candidates compared " + compared);
        kinds.ShouldContain("same trade");
        kinds.ShouldContain("open at end");
        kinds.ShouldContain("pending");
    }

    private static DateOnly SessionDate(long epoch) => XDrishti.Domain.Market.Bars.SessionClock.DateOf(epoch);

    [Fact]
    public void An_earlier_from_changes_no_trade_of_a_later_one()
    {
        var strategyOptions = new ReplayOptions(MinSessionCompleteness: 0.01);
        var compared = 0;
        foreach (var series in All)
        {
            var strategy = Rep.Prepare(BaseBreakoutV1.Create(series.BaseLen, (double)series.BasePct, (double)series.VolX, (double)series.RewardRisk), XDrishti.Domain.Market.Bars.Timeframe.D1, Cand.NoSlippage);
            var later = series.Dates[40 + (series.Seed * 5 % (series.Dates.Count - 50))];
            var early = Rep.Replay(strategy, series.Bars(), series.Dates[0], series.Dates[^1], calendar: series.Calendar, options: strategyOptions);
            var late = Rep.Replay(strategy, series.Bars(), later, series.Dates[^1], calendar: series.Calendar, options: strategyOptions);

            var fromEarly = early.Trades.Concat(early.OpenAtEnd).Where(t => DecisionOf(t) >= later).OrderBy(t => t.EntryEpochSeconds).ToList();
            var fromLate = late.Trades.Concat(late.OpenAtEnd).OrderBy(t => t.EntryEpochSeconds).ToList();
            fromLate.ShouldBe(fromEarly, "seed " + series.Seed);
            compared += fromLate.Count;
        }

        compared.ShouldBeGreaterThanOrEqualTo(10, "trades compared " + compared);
    }

    private static DateOnly DecisionOf(SimTrade trade) => XDrishti.Domain.Market.Bars.SessionClock.DateOf(trade.DecisionEpochSeconds - 60);
}
