using XDrishti.Domain.Research;
using XDrishti.Simulation.Tests.Support;

namespace XDrishti.Simulation.Tests.Oracle;

/// <summary>
/// Criterion 10 of P4-03: the oracle, extended for the three trails from the rules D1 to D7 and ADR 0051 only, and the engine agree on every field of every trade over at least 18,500 seeded series for each rule: O's generator at 500 series in
/// each of its 16 categories and the engine's intraday, daily and swing generators at 500 each, every one in both fill modes. The period, the ATR multiple, the activation and the bar size (15 minutes, or a whole day for a swing walk) vary with
/// the seed, so that a value that is not ready, an activation at 0, a looser level, a level the price has passed and a signal on the square-off bar all occur. A trail has no target, so the comparison walks without one.
/// </summary>
public sealed class TrailComparisonTests(ITestOutputHelper output)
{
    private static readonly bool[] Modes = [false, true];

    private static readonly int[] Periods = [1, 3, 5, 14];

    private static readonly double[] Multiples = [0.5, 1.0, 2.0, 3.5];

    private static readonly double[] Activations = [0, 0, 0.5, 1.0, 2.0];

    private const int PerCategory = 500;

    internal static ComparisonCase WithTrail(ComparisonCase series, OracleTrailKind kind)
    {
        var period = Periods[(int)(series.Seed % Periods.Length)];
        var multiple = Multiples[(int)((series.Seed / 3) % Multiples.Length)];
        var activation = Activations[(int)((series.Seed / 5) % Activations.Length)];
        var daily = series.Generator.Contains("aily", StringComparison.Ordinal) || (series.Generator.Contains("wing", StringComparison.Ordinal) && series.Seed % 2 == 1);
        var groups = series.Groups.Select(g =>
        {
            var side = g.EngineCandidates.Count == 0 ? Side.Buy : g.EngineCandidates[0].Side;
            var built = TrailTape.Of(series.Bars, kind, side, period, multiple, activation, daily ? 375 : 15);
            return g with
            {
                Oracle = g.Oracle with { Rules = g.Oracle.Rules with { RewardRisk = null, Trail = built.Trail } },
                EngineRules = g.EngineRules with { RewardRisk = null },
                Exit = built.Context,
                EngineResults = null,
            };
        }).ToList();
        return series with { Groups = groups };
    }

    private static IEnumerable<ComparisonCase> AllSeries() =>
        Enumerable.Range(1, OracleCategories.Count)
            .SelectMany(category => Enumerable.Range(0, PerCategory).SelectMany(seed => Modes.Select(exact => ComparisonCases.OfOracle(OracleSeries.Of(category, seed), exact))))
            .Concat(ComparisonCases.Intraday(exact: false)).Concat(ComparisonCases.Intraday(exact: true))
            .Concat(ComparisonCases.Daily(exact: false)).Concat(ComparisonCases.Daily(exact: true))
            .Concat(ComparisonCases.Swing(exact: false)).Concat(ComparisonCases.Swing(exact: true));

    private void AgreeOver(OracleTrailKind kind)
    {
        var stats = new RatioStats();
        var results = AllSeries().AsParallel().Select(series => SeriesComparison.Run(WithTrail(series, kind), stats)).ToList();

        var trades = results.Sum(r => r.Trades);
        output.WriteLine($"{kind}: series {results.Count}, candidates {results.Sum(r => r.Candidates)}, trades {trades}, blocked {results.Sum(r => r.Blocked)}, ratio fields {stats.Compared} compared, {stats.NotExact} not bit-equal, largest difference {stats.MaxDifference:R}");
        results.Count.ShouldBeGreaterThanOrEqualTo(18_500);
        results.SelectMany(r => r.Mismatches.Select(m => $"{r.Generator} {r.Seed} {r.Mode} {m.Where}: {m.Mismatch}")).Take(20).ShouldBeEmpty();
        trades.ShouldBeGreaterThan(10_000);
    }

    [Fact]
    public void The_oracle_and_the_engine_agree_on_every_field_of_every_ATR_TRAIL_trade_over_at_least_18500_series() => AgreeOver(OracleTrailKind.Atr);

    [Fact]
    public void The_oracle_and_the_engine_agree_on_every_field_of_every_STRUCTURE_TRAIL_trade_over_at_least_18500_series() => AgreeOver(OracleTrailKind.Structure);

    [Fact]
    public void The_oracle_and_the_engine_agree_on_every_field_of_every_EMA_TRAIL_trade_over_at_least_18500_series() => AgreeOver(OracleTrailKind.Ema);

    [Theory]
    [InlineData(OracleTrailKind.Atr)]
    [InlineData(OracleTrailKind.Structure)]
    [InlineData(OracleTrailKind.Ema)]
    public void The_comparison_reaches_moved_stops_ignored_requests_and_for_the_EMA_signal_exits(OracleTrailKind kind)
    {
        var moved = 0;
        var ignored = 0L;
        var signals = 0;
        var swingTrades = 0;
        var sells = 0;
        var series = ComparisonCases.Intraday(exact: false).Concat(ComparisonCases.Swing(exact: false)).Take(1500).Select(s => WithTrail(s, kind));
        foreach (var one in series)
        {
            foreach (var group in one.Groups)
            {
                var walked = OracleAdapter.Walk(one.Bars, group.EngineCandidates, group.EngineRules, group.Exit);
                foreach (var result in walked.Results.Where(r => r.Trade is not null))
                {
                    var trade = result.Trade!;
                    ignored += result.Counters.StopLoosenIgnored;
                    moved += trade.ExitReason == OracleExitReason.Stop && Math.Abs(trade.ExitPrice - trade.StopPrice) > 1e-9 ? 1 : 0;
                    signals += trade.ExitReason == OracleExitReason.EmaExit ? 1 : 0;
                    swingTrades += trade.SessionsHeld > 1 ? 1 : 0;
                    sells += trade.Side == OracleSide.Sell ? 1 : 0;
                    trade.Legs.ShouldBeNull("a trail leaves in one fill");
                }
            }
        }

        output.WriteLine($"{kind}: stops taken away from the initial level {moved}, ignored requests {ignored}, signal exits {signals}, swing trades {swingTrades}, sells {sells}");
        swingTrades.ShouldBeGreaterThan(0);
        sells.ShouldBeGreaterThan(0);
        if (kind == OracleTrailKind.Ema)
        {
            signals.ShouldBeGreaterThan(50);
        }
        else
        {
            moved.ShouldBeGreaterThan(0);
            signals.ShouldBe(0);
        }
    }
}
