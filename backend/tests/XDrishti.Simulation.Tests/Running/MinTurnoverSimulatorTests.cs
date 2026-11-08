using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Running;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Strategies.Model;
using static XDrishti.Simulation.Tests.Support.TestCalendars;

namespace XDrishti.Simulation.Tests.Running;

/// <summary>CF-05, ADR 0230 through the real simulator: the filter min_turnover is evaluated (no longer refused), blocks and counts what it cannot pass, fails closed without its threshold, and a strategy that does not name it is untouched.</summary>
public sealed class MinTurnoverSimulatorTests
{
    private static readonly StrategyDefinition Named = TestStrategies.OrbBuyOnly() with { Filters = ["min_turnover"] };

    private static FillRules WithThreshold(decimal? crore) => FillRules.Create(0.1, SlippageConfig.None, 0, false, 3m, 8m, crore).Value;

    private static Result<SimulationResult> Run(StrategyDefinition definition, FillRules fills, Func<DateOnly, decimal?>? turnover)
    {
        var simulator = Sim.Create(definition, Timeframe.M15, fills);
        return simulator.Run(Scan.Instrument, Cases.F("F1").Bars, Sim.Tick, DefaultWeek(), null, null, null, null, null, turnover);
    }

    [Fact]
    public void An_entry_with_enough_turnover_is_taken_and_equals_the_trade_of_the_strategy_without_the_filter()
    {
        var filtered = Run(Named, WithThreshold(20m), _ => 20m * LiquidityTiers.Crore);
        var plain = Sim.Run(Sim.Create(TestStrategies.OrbBuyOnly(), fills: Cand.NoSlippage), Cases.F("F1").Bars);

        filtered.IsSuccess.ShouldBeTrue();
        filtered.Value.Trades.ShouldHaveSingleItem();
        OutcomeFormatter.Render(Result.Success(filtered.Value.Outcomes.Single().Walk)).ShouldBe(OutcomeFormatter.Render(Result.Success(plain.Outcomes.Single().Walk)));
        filtered.Value.Walk.TurnoverBlocked.ShouldBe(0);
    }

    [Fact]
    public void Thin_unknown_and_missing_turnover_block_the_entry_and_count_it()
    {
        foreach (Func<DateOnly, decimal?>? lookup in new Func<DateOnly, decimal?>?[] { _ => 19m * LiquidityTiers.Crore, _ => null, null })
        {
            var result = Run(Named, WithThreshold(20m), lookup);

            result.IsSuccess.ShouldBeTrue();
            result.Value.Trades.ShouldBeEmpty();
            (result.Value.Walk.Candidates, result.Value.Walk.TurnoverBlocked).ShouldBe((1L, 1L));
        }
    }

    [Fact]
    public void A_strategy_that_names_the_filter_without_a_threshold_in_its_fill_rules_is_refused_not_run_as_if_it_passed()
    {
        var result = Run(Named, WithThreshold(null), _ => 100m * LiquidityTiers.Crore);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("simulation.filter_not_available");
    }

    [Fact]
    public void A_strategy_that_does_not_name_the_filter_gives_the_same_results_whatever_lookup_it_is_handed()
    {
        var plain = TestStrategies.OrbBuyOnly();

        var withLookup = Run(plain, Cand.NoSlippage, _ => 0m);
        var without = Run(plain, Cand.NoSlippage, null);

        OutcomeFormatter.Render(Result.Success(withLookup.Value.Outcomes.Single().Walk)).ShouldBe(OutcomeFormatter.Render(Result.Success(without.Value.Outcomes.Single().Walk)));
        withLookup.Value.Walk.TurnoverBlocked.ShouldBe(0);
    }
}
