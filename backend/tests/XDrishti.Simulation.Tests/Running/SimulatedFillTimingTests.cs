using XDrishti.Domain.Research;
using XDrishti.Simulation.Running;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Simulation.Walking;
using XDrishti.Strategies.BuiltIn;

namespace XDrishti.Simulation.Tests.Running;

/// <summary>
/// Criterion 16 of P3-03a: over the seeded series of the look-ahead property, every trade of <c>Simulator.Run</c> satisfies the properties of P3-03 AC9 (fill minute, entry price, exit price bounds, R recomputed from the stored
/// prices; <see cref="FillProperties"/> is the one copy of them) and its strategy id, side, date and trigger bar equal those of its candidate.
/// </summary>
public sealed class SimulatedFillTimingTests
{
    private const int SquareOff = 15 * 60 + 15;

    private static readonly Simulator Orb = Sim.Create(OrbV1.Create());

    private static readonly List<(ulong Seed, SimulationResult Result, XDrishti.Domain.Market.Bars.Bar[] Bars)> Runs =
        [.. Enumerable.Range(1, 100).Select(i => (ulong)i).Select(seed => (seed, Sim.Run(Orb, SeededSeries.Of(seed)), SeededSeries.Of(seed)))];

    private static IEnumerable<(string Label, CandidateOutcome Outcome, SimTrade Trade, XDrishti.Domain.Market.Bars.Bar[] Bars, int Settled)> Trades() =>
        Runs.SelectMany(run => run.Result.Outcomes.Where(o => o.Walk.Trade is not null)
            .Select(o => ("seed " + run.Seed, o, o.Walk.Trade!, run.Bars, WalkProperties.SettledAt(o.Candidate, run.Bars, o.Walk))));

    [Fact]
    public void Enough_trades_are_checked_on_both_sides_and_with_every_kind_of_exit()
    {
        var trades = Trades().Select(t => t.Trade).ToList();

        trades.Count.ShouldBeGreaterThanOrEqualTo(60);
        trades.Count(t => t.Side == Side.Buy).ShouldBeGreaterThanOrEqualTo(20);
        trades.Count(t => t.Side == Side.Sell).ShouldBeGreaterThanOrEqualTo(20);
        trades.Select(t => t.ExitReason).Distinct().Count().ShouldBeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public void Every_trade_fills_in_the_minute_after_its_candidate_at_its_open_within_the_delay_and_before_the_square_off()
    {
        foreach (var (_, outcome, trade, bars, _) in Trades())
        {
            FillProperties.AssertFill(trade, outcome.Candidate, bars, outcome.Walk, SquareOff);
        }
    }

    [Fact]
    public void Every_exit_price_lies_in_its_minute_or_is_the_open_or_the_close_the_rule_names()
    {
        foreach (var (label, outcome, trade, bars, settled) in Trades())
        {
            FillProperties.AssertExit(trade, bars, outcome.Walk, settled, FillProperties.AssertFill(trade, outcome.Candidate, bars, outcome.Walk, SquareOff), SquareOff, label);
        }
    }

    [Fact]
    public void R_and_the_excursions_equal_a_plain_reference_loop_from_the_stored_prices()
    {
        foreach (var (label, outcome, trade, bars, settled) in Trades())
        {
            FillProperties.AssertExcursions(trade, bars, outcome.Walk, settled, FillProperties.AssertFill(trade, outcome.Candidate, bars, outcome.Walk, SquareOff), label);
        }
    }

    [Fact]
    public void Every_trade_names_the_strategy_side_date_and_trigger_bar_of_its_candidate()
    {
        foreach (var (label, outcome, trade, _, _) in Trades())
        {
            var candidate = outcome.Candidate;

            trade.StrategyId.ShouldBe(candidate.StrategyId, label);
            trade.Side.ShouldBe(candidate.Side, label);
            trade.TradeDate.ShouldBe(XDrishti.Domain.Market.Bars.SessionClock.DateOf(candidate.DecisionEpochSeconds - 60), label);
            trade.TriggerBar.ShouldBe(candidate.TriggerBar, label);
            trade.TriggerBarMinutes.ShouldBe(candidate.TriggerBarMinutes, label);
            trade.DecisionEpochSeconds.ShouldBe(candidate.DecisionEpochSeconds, label);
            trade.InstrumentId.ShouldBe(candidate.Instrument.Id, label);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Every_walk_of_the_simulator_equals_the_walker_on_the_same_candidate_with_the_fill_rules_and_tick_of_the_run(bool withoutSlippage)
    {
        var fills = withoutSlippage ? Cand.NoSlippage : FillRules.Default;
        var simulator = Sim.Create(OrbV1.Create(), fills: fills);
        var rules = new WalkRules(2.0, SquareOff, fills, Sim.Tick);

        foreach (var run in Runs)
        {
            var result = Sim.Run(simulator, run.Bars);
            foreach (var outcome in result.Outcomes)
            {
                var alone = TradeWalker.Walk(outcome.Candidate, run.Bars, rules);

                OutcomeFormatter.Render(XDrishti.Domain.Common.Result.Success(outcome.Walk)).ShouldBe(OutcomeFormatter.Render(alone), "seed " + run.Seed);
            }
        }
    }

    [Fact]
    public void The_trades_of_a_run_without_slippage_have_filled_prices_equal_to_the_exact_ones_and_those_with_slippage_never_beat_them()
    {
        var exact = Sim.Create(OrbV1.Create(), fills: Cand.NoSlippage);

        foreach (var run in Runs)
        {
            foreach (var trade in Sim.Run(exact, run.Bars).Outcomes.Select(o => o.Walk.Trade).OfType<SimTrade>())
            {
                (trade.RFilled, trade.SlippageR, trade.Fills).ShouldBe((trade.RExact, 0, FillBasis.Exact), "seed " + run.Seed);
            }

            foreach (var trade in run.Result.Outcomes.Select(o => o.Walk.Trade).OfType<SimTrade>())
            {
                trade.Fills.ShouldBe(FillBasis.Modelled);
                trade.RFilled.ShouldBeLessThanOrEqualTo(trade.RExact + 1e-12, "seed " + run.Seed);
            }
        }
    }
}
