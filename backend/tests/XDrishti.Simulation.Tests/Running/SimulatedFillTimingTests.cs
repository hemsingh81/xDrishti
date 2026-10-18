using XDrishti.Domain.Research;
using XDrishti.Simulation.Running;
using XDrishti.Simulation.Tests.Support;
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
            .Select(o => ("seed " + run.Seed, o, o.Walk.Trade!, run.Bars, LookAhead.SettledAt(o.Candidate, run.Bars, o.Walk))));

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
            FillProperties.AssertExit(trade, bars, outcome.Walk, settled, outcome.Candidate.DecisionBarIndex + 1, SquareOff, label);
        }
    }

    [Fact]
    public void R_and_the_excursions_equal_a_plain_reference_loop_from_the_stored_prices()
    {
        foreach (var (label, outcome, trade, bars, settled) in Trades())
        {
            FillProperties.AssertExcursions(trade, bars, outcome.Walk, settled, outcome.Candidate.DecisionBarIndex + 1, label);
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
}
