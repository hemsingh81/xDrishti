using XDrishti.Domain.Research;
using XDrishti.Simulation.Tests.Support;

namespace XDrishti.Simulation.Tests.Oracle;

/// <summary>
/// Criterion 7 of P4-04: the oracle, extended for <c>PYRAMID</c> from the rule table D1 to D10 and ADR 0052 only, and the engine agree on every field of every trade, the legs and the counters of the adds included, over at least 18,500 seeded
/// series: O's generator at 500 series in each of its 16 categories and the engine's intraday, daily and swing generators at 500 each, every one in both fill modes. The ladder, <c>max_adds</c>, the ATR multiple, the period, the full size (unknown,
/// or so small that an add holds no whole share) and the <c>time_stop</c> overlay vary with the seed.
/// </summary>
public sealed class PyramidComparisonTests(ITestOutputHelper output)
{
    internal static ComparisonCase WithPyramid(ComparisonCase series)
    {
        var daily = series.Generator.Contains("aily", StringComparison.Ordinal) || (series.Generator.Contains("wing", StringComparison.Ordinal) && series.Seed % 2 == 1);
        var groups = series.Groups.Select(g =>
        {
            var side = g.EngineCandidates.Count == 0 ? Side.Buy : g.EngineCandidates[0].Side;
            var hold = g.Oracle.Rules.Hold?.MaxHoldSessions ?? 0;
            var variant = PyramidVariant.Of(series.Seed, hold);
            var built = PyramidTape.Of(series.Bars, side, variant, daily ? 375 : 15);
            return g with
            {
                Oracle = g.Oracle with { Rules = g.Oracle.Rules with { RewardRisk = null, Trail = built.Trail, Pyramid = built.Pyramid, Policy = built.Policy } },
                EngineRules = g.EngineRules with { RewardRisk = null },
                Exit = built.Context,
                EngineResults = null,
            };
        }).ToList();
        return series with { Groups = groups };
    }

    [Fact]
    public void The_oracle_and_the_engine_agree_on_every_field_of_every_PYRAMID_trade_over_at_least_18500_series()
    {
        var stats = new RatioStats();
        var results = TrailComparisonTests.AllSeries().AsParallel().Select(series => SeriesComparison.Run(WithPyramid(series), stats)).ToList();

        var trades = results.Sum(r => r.Trades);
        output.WriteLine($"PYRAMID: series {results.Count}, candidates {results.Sum(r => r.Candidates)}, trades {trades}, blocked {results.Sum(r => r.Blocked)}, ratio fields {stats.Compared} compared, {stats.NotExact} not bit-equal, largest difference {stats.MaxDifference:R}");
        results.Count.ShouldBeGreaterThanOrEqualTo(18_500);
        results.SelectMany(r => r.Mismatches.Select(m => $"{r.Generator} {r.Seed} {r.Mode} {m.Where}: {m.Mismatch}")).Take(20).ShouldBeEmpty();
        trades.ShouldBeGreaterThan(10_000);
    }

    [Fact]
    public void The_comparison_reaches_adds_taken_missed_and_skipped_for_quantity_in_buys_sells_and_swings()
    {
        long taken = 0, missed = 0, skippedRisk = 0, skippedQuantity = 0, ignored = 0;
        var withAdds = 0;
        var twoAdds = 0;
        var sells = 0;
        var swings = 0;
        var timeStops = 0;
        foreach (var one in TrailComparisonTests.AllSeries().Where((_, i) => i % 3 == 0).Select(WithPyramid))
        {
            foreach (var group in one.Groups)
            {
                var walked = OracleAdapter.Walk(one.Bars, group.EngineCandidates, group.EngineRules, group.Exit);
                foreach (var result in walked.Results.Where(r => r.Trade is not null))
                {
                    var trade = result.Trade!;
                    var c = result.Counters;
                    taken += c.AddsTaken;
                    missed += c.AddsMissed;
                    skippedRisk += c.AddsSkippedRisk;
                    skippedQuantity += c.AddsSkippedQuantity;
                    ignored += c.StopLoosenIgnored;
                    withAdds += c.AddsTaken > 0 ? 1 : 0;
                    twoAdds += c.AddsTaken > 1 ? 1 : 0;
                    sells += c.AddsTaken > 0 && trade.Side == OracleSide.Sell ? 1 : 0;
                    swings += c.AddsTaken > 0 && group.EngineRules.Hold is not null ? 1 : 0;
                    timeStops += trade.ExitReason == OracleExitReason.TimeStop && c.AddsTaken > 0 ? 1 : 0;
                    trade.Legs.ShouldNotBeNull("a pyramid always has legs");
                    trade.Legs.Count(l => l.Kind == "Add").ShouldBe(c.AddsTaken);
                }
            }
        }

        output.WriteLine($"PYRAMID: adds taken {taken}, missed {missed}, skipped by risk {skippedRisk}, skipped by quantity {skippedQuantity}, trades with an add {withAdds} (two or more {twoAdds}), sells {sells}, swings {swings}, time stops after an add {timeStops}, loosening requests {ignored}");
        taken.ShouldBeGreaterThan(100);
        missed.ShouldBeGreaterThan(0);
        skippedRisk.ShouldBe(0, "the stop in force always holds the open risk to one risk and an add is made only above it, so the risk rule always has room (ADR 0052, decision 4)");
        skippedQuantity.ShouldBeGreaterThan(0);
        twoAdds.ShouldBeGreaterThan(20);
        sells.ShouldBeGreaterThan(20);
        swings.ShouldBeGreaterThan(20);
    }
}
