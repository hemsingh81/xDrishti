using XDrishti.Simulation.Tests.Support;

namespace XDrishti.Simulation.Tests.Oracle;

/// <summary>
/// Criterion 10 of P4-01: the oracle, extended for legs from the rules D4 to D11 and the test-only rule D14, and the engine agree on every field of every trade, the legs included, over at least 18,500 seeded series: O's generator at 500
/// series in each of its 16 categories and the engine's intraday, daily and swing generators at 500 each, every one in both fill modes. The share the rule takes out and whether it also asks for a looser stop vary with the seed, so
/// that three shares and the ignored-request counter are covered.
/// </summary>
public sealed class LadderComparisonTests(ITestOutputHelper output)
{
    private static readonly bool[] Modes = [false, true];

    private static readonly decimal[] Shares = [0.5m, 0.25m, 0.4m];

    private const int PerCategory = 500;

    internal static ComparisonCase WithLadder(ComparisonCase series)
    {
        var share = Shares[(int)(series.Seed % Shares.Length)];
        var askLoose = series.Seed % 2 == 1;
        var built = LadderTape.Of(series.Bars, share, askLoose);
        var groups = series.Groups.Select(g => g with { Oracle = g.Oracle with { Rules = g.Oracle.Rules with { Ladder = built.Ladder } }, Exit = built.Context, EngineResults = null }).ToList();
        return series with { Groups = groups };
    }

    private static IEnumerable<ComparisonCase> AllSeries() =>
        Enumerable.Range(1, OracleCategories.Count)
            .SelectMany(category => Enumerable.Range(0, PerCategory).SelectMany(seed => Modes.Select(exact => ComparisonCases.OfOracle(OracleSeries.Of(category, seed), exact))))
            .Concat(ComparisonCases.Intraday(exact: false)).Concat(ComparisonCases.Intraday(exact: true))
            .Concat(ComparisonCases.Daily(exact: false)).Concat(ComparisonCases.Daily(exact: true))
            .Concat(ComparisonCases.Swing(exact: false)).Concat(ComparisonCases.Swing(exact: true));

    [Fact]
    public void The_oracle_and_the_engine_agree_on_every_field_of_every_trade_with_legs_over_at_least_18500_series()
    {
        var stats = new RatioStats();
        var results = AllSeries().AsParallel().Select(series => SeriesComparison.Run(WithLadder(series), stats)).ToList();

        var trades = results.Sum(r => r.Trades);
        output.WriteLine($"series {results.Count}, candidates {results.Sum(r => r.Candidates)}, trades {trades}, blocked {results.Sum(r => r.Blocked)}, ratio fields {stats.Compared} compared, {stats.NotExact} not bit-equal, largest difference {stats.MaxDifference:R}");
        results.Count.ShouldBeGreaterThanOrEqualTo(18_500);
        results.SelectMany(r => r.Mismatches.Select(m => $"{r.Generator} {r.Seed} {r.Mode} {m.Where}: {m.Mismatch}")).Take(20).ShouldBeEmpty();
        trades.ShouldBeGreaterThan(10_000);
    }

    [Fact]
    public void The_comparison_reaches_trades_with_legs_stops_taken_away_from_the_initial_level_and_ignored_requests_to_loosen()
    {
        var legs = 0;
        var moved = 0;
        var ignored = 0L;
        var engineCases = ComparisonCases.Intraday(exact: false).Concat(ComparisonCases.Swing(exact: false)).Take(1000).Select(WithLadder);
        foreach (var series in engineCases)
        {
            foreach (var group in series.Groups)
            {
                var walked = OracleAdapter.Walk(series.Bars, group.EngineCandidates, group.EngineRules, group.Exit);
                foreach (var result in walked.Results.Where(r => r.Trade is not null))
                {
                    legs += result.Trade!.Legs is null ? 0 : 1;
                    ignored += result.Counters.StopLoosenIgnored;
                    moved += result.Trade.ExitReason == OracleExitReason.Stop && Math.Abs(result.Trade.ExitPrice - result.Trade.StopPrice) > 1e-9 ? 1 : 0;
                }
            }
        }

        output.WriteLine($"trades with legs {legs}, stops taken away from the initial level (a moved level or a gap) {moved}, ignored requests to loosen {ignored}");
        legs.ShouldBeGreaterThan(50);
        moved.ShouldBeGreaterThan(0);
        ignored.ShouldBeGreaterThan(0);
    }
}
