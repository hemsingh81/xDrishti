using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Tests.Support;

namespace XDrishti.Simulation.Tests.Strategies;

/// <summary>
/// Criterion 14 of P3-05: the plain reference loop of D12 reproduces 50 seeded days (25 seeds of 2 days) exactly, candidates and trades field by field; the floors are asserted so that the check cannot pass on empty days; the loop uses no production
/// helper (its source is read); and three mutants of the loop each differ from the engine on at least 10 of the 50 days while the unmutated loop differs on none.
/// </summary>
public sealed partial class VwapPullbackReferenceLoopTests
{
    private static readonly ulong[] Seeds = [.. Enumerable.Range(1, 25).Select(i => (ulong)i)];

    private static Bar[] Series(ulong seed) => VwapSeeded.Of(seed, days: 2);

    [GeneratedRegex(@"XDrishti\.(Indicators|Strategies|Simulation(?!\.Tests))|\b(Simulator|EntryScanner|TradeWalker|TickGrid|StrategyCompiler|StrategyDefinition|VwapPullback|OrbV1|BuiltInStrategies|FeatureFrame)\b", RegexOptions.CultureInvariant)]
    private static partial Regex Forbidden();

    private static string LoopSource([CallerFilePath] string here = "") => File.ReadAllText(Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(here))!, "Support", "ReferenceLoop.cs"));

    [Fact]
    public void The_loop_reproduces_the_candidates_and_the_trades_of_the_engine_on_every_seeded_day()
    {
        var differing = new List<string>();
        foreach (var seed in Seeds)
        {
            var bars = Series(seed);
            var pull = VwapSeeded.PullAtr(seed);
            var engine = VwapEngine.Run(bars, pull);
            var (candidates, trades) = VwapEngine.Lines(ReferenceLoop.Run(bars, pull));
            VwapEngine.DaysDiffering(bars, pull).ShouldBe(0, "seed " + seed + ": the counters of the five conditions per date\n" + string.Join("\n", VwapEngine.Last ?? []));
            var engineCandidates = VwapEngine.Candidates(engine);
            var engineTrades = VwapEngine.Trades(engine);
            if (!engineCandidates.SequenceEqual(candidates) || !engineTrades.SequenceEqual(trades))
            {
                differing.Add("seed " + seed + "\nengine " + string.Join(" | ", engineCandidates) + "\nloop   " + string.Join(" | ", candidates) + "\nengine " + string.Join(" | ", engineTrades) + "\nloop   " + string.Join(" | ", trades));
            }
        }

        differing.ShouldBeEmpty(string.Join("\n", differing));
    }

    [Fact]
    public void The_seeded_days_are_rich_enough_for_the_comparison_to_mean_something()
    {
        var withCandidate = 0;
        var buys = 0;
        var sells = 0;
        var laterTriggers = 0;
        var reasons = new Dictionary<ExitReason, int>();
        foreach (var seed in Seeds)
        {
            var result = VwapEngine.Run(Series(seed), VwapSeeded.PullAtr(seed));
            var dates = result.Outcomes.Select(o => Render.Instant(o.Candidate.DecisionEpochSeconds)[..10]).Distinct().Count();
            withCandidate += dates;
            buys += result.Outcomes.Count(o => o.Candidate.Side == Side.Buy);
            sells += result.Outcomes.Count(o => o.Candidate.Side == Side.Sell);
            laterTriggers += result.Scans.Sum(s => (int)s.LaterTriggers) > 0 ? 1 : 0;
            foreach (var trade in result.Trades)
            {
                reasons[trade.ExitReason] = reasons.GetValueOrDefault(trade.ExitReason) + 1;
            }
        }

        var seen = $"days with a candidate {withCandidate}, buys {buys}, sells {sells}, seeds with later triggers {laterTriggers}, stops {reasons.GetValueOrDefault(ExitReason.Stop)}, targets {reasons.GetValueOrDefault(ExitReason.Target)}, square-offs {reasons.GetValueOrDefault(ExitReason.SquareOff)}";
        withCandidate.ShouldBeGreaterThanOrEqualTo(25, seen);
        buys.ShouldBeGreaterThanOrEqualTo(8, seen);
        sells.ShouldBeGreaterThanOrEqualTo(8, seen);
        laterTriggers.ShouldBeGreaterThanOrEqualTo(5, seen);
        reasons.GetValueOrDefault(ExitReason.Stop).ShouldBeGreaterThanOrEqualTo(3, seen);
        reasons.GetValueOrDefault(ExitReason.Target).ShouldBeGreaterThanOrEqualTo(3, seen);
        reasons.GetValueOrDefault(ExitReason.SquareOff).ShouldBeGreaterThanOrEqualTo(3, seen);
    }

    [Fact]
    public void The_loop_source_uses_no_production_helper()
    {
        var source = LoopSource();

        Forbidden().Matches(source).Select(m => m.Value).ShouldBeEmpty();
        source.ShouldContain("namespace XDrishti.Simulation.Tests.Support;");
    }

    [Theory]
    [InlineData("VwapLagReadAsCurrent")]
    [InlineData("LowLaggedByOne")]
    [InlineData("HourlyStubDropped")]
    public void A_mutant_of_the_loop_differs_from_the_engine_on_at_least_ten_of_the_fifty_days_and_the_honest_loop_on_none(string mutantName)
    {
        var mutant = Enum.Parse<ReferenceLoop.Mutant>(mutantName);
        var honest = Seeds.Sum(s => VwapEngine.DaysDiffering(Series(s), VwapSeeded.PullAtr(s)));
        var mutated = Seeds.Sum(s => VwapEngine.DaysDiffering(Series(s), VwapSeeded.PullAtr(s), mutant));

        honest.ShouldBe(0);
        mutated.ShouldBeGreaterThanOrEqualTo(10, mutant + " differs on " + mutated + " of the 50 days");
    }
}
