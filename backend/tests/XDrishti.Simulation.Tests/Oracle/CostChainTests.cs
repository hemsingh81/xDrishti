namespace XDrishti.Simulation.Tests.Oracle;

/// <summary>
/// Annex C of P3-08 (criteria C1 and C4), the chain from the walks (AC17): the oracle's walk feeds the oracle's costing and the engine's walk feeds <c>TradeCostApplier</c>, over the seeded days (both fill modes: the costed days of
/// <c>CostedPropertyTests</c>), the seeded daily series and the swing series, and the two agree on every compared field; and <c>RNet &lt;= RFilled &lt;= RExact</c> with <c>RNet = RFilled</c> only when the total charge is 0 holds on the
/// oracle's costing as well as the engine's on every kept trade of every cost run.
/// </summary>
public sealed class CostChainTests(ITestOutputHelper output)
{
    [Fact]
    public void The_chain_holds_on_the_oracle_s_costing_and_the_engine_s_for_every_kept_trade_of_both_runs()
    {
        foreach (var tally in CostCorpus.All)
        {
            output.WriteLine($"{tally.Name}: {tally.ChainChecked} costings checked, {tally.ChainBroken} broke the chain");
            tally.ChainChecked.ShouldBeGreaterThan(10_000);
            tally.ChainBroken.ShouldBe(0, tally.Name);
        }
    }

    [Fact]
    public void The_chain_run_is_built_from_the_seeded_days_the_daily_series_and_the_swing_series_in_both_fill_modes()
    {
        // the engine's trades come from the generators, not from the author's: all three families and both modes cost something
        var trades = new[] { ComparisonCases.Intraday(exact: false), ComparisonCases.Intraday(exact: true), ComparisonCases.Daily(exact: false), ComparisonCases.Daily(exact: true), ComparisonCases.Swing(exact: false), ComparisonCases.Swing(exact: true) }
            .Select(cases => cases.SelectMany(c => c.Groups.SelectMany(g => CostCorpus.EngineTrades(c.Bars, g.EngineCandidates, g.EngineRules))).Count(t => t is not null))
            .ToArray();

        output.WriteLine("trades per family and mode: " + string.Join(", ", trades));
        trades.ShouldAllBe(n => n > 500);
    }

    [Fact]
    public void The_costed_seeded_days_have_both_sides_and_both_fill_modes()
    {
        var trades = ComparisonCases.Intraday(exact: false).Concat(ComparisonCases.Intraday(exact: true))
            .SelectMany(c => c.Groups.SelectMany(g => CostCorpus.EngineTrades(c.Bars, g.EngineCandidates, g.EngineRules)))
            .OfType<XDrishti.Domain.Research.SimTrade>()
            .ToList();

        trades.Count.ShouldBeGreaterThan(1_500);
        trades.Select(t => t.Side).Distinct().Count().ShouldBe(2);
        trades.Select(t => t.Fills).Distinct().Count().ShouldBe(2);
    }
}
