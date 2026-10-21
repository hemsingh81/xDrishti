using XDrishti.Simulation.Tests.Support;

namespace XDrishti.Simulation.Tests.Oracle;

/// <summary>
/// Criteria 3 and 10 of P4-02: the oracle, extended for the three rules from the tables D1 to D9 and ADR 0050 only, and the engine agree on every field of every trade, the legs included, over at least 18,500 seeded series for each
/// family (<c>PARTIAL_BE</c>, <c>PARTIAL_TRAIL</c>, <c>FIXED_RR</c> with the <c>time_stop</c>): O's generator at 500 series in each of its 16 categories and the engine's intraday, daily and swing generators at 500 each, every one in both
/// fill modes. The book, T1, T2, the multiple, the minimum progress and the number of bars or sessions vary with the seed; every third seed of the partial families also has the time stop; a NaN ATR occurs in every series.
/// </summary>
public sealed class PolicyComparisonTests(ITestOutputHelper output)
{
    internal static ComparisonCase WithPolicy(ComparisonCase series, PolicyFamily family)
    {
        var groups = series.Groups.Select(group =>
        {
            var hold = group.Oracle.Rules.Hold?.MaxHoldSessions ?? 0;
            var variant = PolicyVariant.Of(family, series.Seed, hold);
            var built = PolicyTape.Of(series.Bars, variant);
            var reward = variant.RewardRisk(group.EngineRules.RewardRisk);
            return group with
            {
                Oracle = group.Oracle with { Rules = group.Oracle.Rules with { RewardRisk = reward, Policy = built.Policy } },
                EngineRules = group.EngineRules with { RewardRisk = reward },
                Exit = built.Context,
                EngineResults = null,
            };
        }).ToList();
        return series with { Groups = groups };
    }

    private void Agree(PolicyFamily family)
    {
        var stats = new RatioStats();
        var results = LadderComparisonTests.AllSeries().AsParallel().Select(series => SeriesComparison.Run(WithPolicy(series, family), stats)).ToList();

        var trades = results.Sum(r => r.Trades);
        output.WriteLine($"{family}: series {results.Count}, candidates {results.Sum(r => r.Candidates)}, trades {trades}, blocked {results.Sum(r => r.Blocked)}, ratio fields {stats.Compared} compared, {stats.NotExact} not bit-equal, largest difference {stats.MaxDifference:R}");
        results.Count.ShouldBeGreaterThanOrEqualTo(18_500);
        results.SelectMany(r => r.Mismatches.Select(m => $"{r.Generator} {r.Seed} {r.Mode} {m.Where}: {m.Mismatch}")).Take(20).ShouldBeEmpty();
        trades.ShouldBeGreaterThan(10_000);
    }

    [Fact]
    public void The_oracle_and_the_engine_agree_on_every_field_of_every_trade_of_PARTIAL_BE_over_at_least_18500_series() => Agree(PolicyFamily.Be);

    [Fact]
    public void The_oracle_and_the_engine_agree_on_every_field_of_every_trade_of_PARTIAL_TRAIL_over_at_least_18500_series() => Agree(PolicyFamily.Trail);

    [Fact]
    public void The_oracle_and_the_engine_agree_on_every_field_of_every_trade_of_the_time_stop_over_at_least_18500_series() => Agree(PolicyFamily.TimeStop);

    [Theory]
    [InlineData(PolicyFamily.Be)]
    [InlineData(PolicyFamily.Trail)]
    [InlineData(PolicyFamily.TimeStop)]
    public void The_comparison_reaches_the_cases_of_D12_for_each_family(PolicyFamily family)
    {
        var t1Legs = 0;
        var timeStops = 0;
        var timeStopsAtLastClose = 0;
        var ignored = 0L;
        var sells = 0;
        var swings = 0;
        var secondTarget = 0;
        var tapeNaN = 0;
        foreach (var series in LadderComparisonTests.AllSeries().Where((_, i) => i % 3 == 0).Select(s => WithPolicy(s, family)))
        {
            tapeNaN += series.Groups.Any(g => g.Oracle.Rules.Policy!.Bars.Any(b => double.IsNaN(b.Atr))) ? 1 : 0;
            foreach (var group in series.Groups)
            {
                var walked = OracleAdapter.Walk(series.Bars, group.EngineCandidates, group.EngineRules, group.Exit);
                foreach (var result in walked.Results.Where(r => r.Trade is not null))
                {
                    var trade = result.Trade!;
                    t1Legs += trade.Legs?.Any(l => l.Reason == "T1") == true ? 1 : 0;
                    timeStops += trade.ExitReason == OracleExitReason.TimeStop ? 1 : 0;
                    timeStopsAtLastClose += result.Counters.TimeStopAtLastClose;
                    ignored += result.Counters.StopLoosenIgnored;
                    sells += trade.Legs is not null && trade.Side == OracleSide.Sell ? 1 : 0;
                    swings += trade.Legs is not null && group.EngineRules.Hold is not null ? 1 : 0;
                    secondTarget += trade.Legs is not null && trade.ExitReason == OracleExitReason.Target ? 1 : 0;
                }
            }
        }

        output.WriteLine($"{family}: T1 legs {t1Legs}, time stops {timeStops} (at the last close {timeStopsAtLastClose}), stops looser than the one in force ignored {ignored}, sells with legs {sells}, swings with legs {swings}, second targets {secondTarget}, series with a NaN ATR {tapeNaN}");
        tapeNaN.ShouldBeGreaterThan(100);
        if (family != PolicyFamily.TimeStop)
        {
            t1Legs.ShouldBeGreaterThan(100);
            sells.ShouldBeGreaterThan(20);
            swings.ShouldBeGreaterThan(20);
        }

        if (family == PolicyFamily.Be)
        {
            secondTarget.ShouldBeGreaterThan(5);
        }

        if (family == PolicyFamily.Trail)
        {
            ignored.ShouldBeGreaterThan(0);
        }

        if (family == PolicyFamily.TimeStop)
        {
            timeStops.ShouldBeGreaterThan(100);
        }
    }
}
