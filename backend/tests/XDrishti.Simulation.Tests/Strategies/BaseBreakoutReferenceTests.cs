using System.Globalization;
using System.Runtime.CompilerServices;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Running;
using XDrishti.Simulation.Tests.Support;

namespace XDrishti.Simulation.Tests.Strategies;

/// <summary>
/// Criterion 11 of P3-06: over 50 generated series (<see cref="SeededBase"/>, sparse days, exact fills) the decisions and the fates of <c>Simulator.Run</c> equal those of the plain loop <see cref="BaseBreakoutReference"/>, line for line and in order. The
/// reference series have no missing 15:29 minute (the stale-bar cases are the hand rows P9 and the counters of the look-ahead series). Each planted case occurs in at least 5 of the 50 series.
/// </summary>
public sealed class BaseBreakoutReferenceTests
{
    private const int Series = 50;

    private static readonly BaseSeries[] All = [.. Enumerable.Range(1, Series).Select(SeededBase.Make)];

    private static string EngineLine(CandidateOutcome o)
    {
        var date = SessionClock.DateOf(o.Candidate.DecisionEpochSeconds - 60);
        var head = string.Create(CultureInfo.InvariantCulture, $"{date:yyyy-MM-dd} close={Money((decimal)o.Candidate.TriggerBar.Close)} stop={Money((decimal)o.Candidate.StopLevel)}");
        if (o.Walk.Trade is not { } t)
        {
            return head + " " + o.Walk.NoTrade;
        }

        return head + string.Create(
            CultureInfo.InvariantCulture,
            $" Trade fill={t.TradeDate:yyyy-MM-dd}@{Money((decimal)t.EntryPrice)} exit={SessionClock.DateOf(t.ExitEpochSeconds):yyyy-MM-dd}@{Money((decimal)t.ExitPrice)} {t.ExitReason} R={Math.Round(t.RExact, 6)}");
    }

    private static string ReferenceLine(BaseBreakoutReference.Fate f)
    {
        var head = string.Create(CultureInfo.InvariantCulture, $"{f.Decision.Date:yyyy-MM-dd} close={Money(f.Decision.TriggerClose)} stop={Money(f.Decision.Stop)}");
        return f.Kind != "Trade"
            ? head + " " + f.Kind
            : head + string.Create(CultureInfo.InvariantCulture, $" Trade fill={f.FillDate:yyyy-MM-dd}@{Money(f.Entry)} exit={f.ExitDate:yyyy-MM-dd}@{Money(f.Exit)} {f.Reason} R={Math.Round(f.RExact, 6)}");
    }

    private static string Money(decimal value) => Math.Round(value, 6).ToString("0.######", CultureInfo.InvariantCulture);

    [Fact]
    public void The_decisions_and_the_fates_of_the_simulator_equal_those_of_the_plain_loop_on_fifty_series()
    {
        var compared = 0;
        foreach (var series in All)
        {
            var engine = BaseRun.Of(series).Outcomes.Select(EngineLine).ToList();
            var reference = BaseBreakoutReference.Run(series).Select(ReferenceLine).ToList();

            engine.ShouldBe(reference, "seed " + series.Seed);
            compared += engine.Count;
        }

        compared.ShouldBeGreaterThanOrEqualTo(40, "decisions compared " + compared);
    }

    [Fact]
    public void The_series_give_every_kind_of_fate_and_every_exit_reason_often_enough_to_mean_something()
    {
        var fates = All.SelectMany(BaseBreakoutReference.Run).ToList();

        var kinds = fates.GroupBy(f => f.Kind == "Trade" ? f.Reason : f.Kind).ToDictionary(g => g.Key, g => g.Count());
        var seen = string.Join(", ", kinds.OrderBy(k => k.Key, StringComparer.Ordinal).Select(k => k.Key + " " + k.Value));
        foreach (var kind in new[] { "Target", "Stop", "MaxHold", "LimitNotReached", "PositionOpen" })
        {
            kinds.GetValueOrDefault(kind).ShouldBeGreaterThanOrEqualTo(3, kind + " in " + seen);
        }

        fates.Count(f => f.Kind == "Trade").ShouldBeGreaterThanOrEqualTo(25, seen);
    }

    [Theory]
    [InlineData("plant")]
    [InlineData("holiday")]
    [InlineData("gap-above-limit")]
    [InlineData("gap-through-stop")]
    [InlineData("entry-through-stop")]
    [InlineData("friday-signal")]
    [InlineData("wide-base")]
    [InlineData("tie")]
    [InlineData("weak-volume")]
    public void Each_planted_case_occurs_in_at_least_five_of_the_fifty_series(string tag)
    {
        All.Count(s => s.Tags.Contains(tag)).ShouldBeGreaterThanOrEqualTo(5, tag);
    }

    [Fact]
    public void The_generated_gaps_and_a_friday_signal_are_seen_by_the_engine_not_only_planted()
    {
        var outcomes = All.SelectMany(s => BaseRun.Of(s).Outcomes.Select(o => (Series: s, Outcome: o))).ToList();

        outcomes.Count(x => x.Outcome.Walk.NoTrade == NoTradeReason.EntryThroughStop).ShouldBeGreaterThanOrEqualTo(1);
        outcomes.Count(x => x.Outcome.Walk.StopGapExit).ShouldBeGreaterThanOrEqualTo(1);
        outcomes.Count(x => SessionClock.DateOf(x.Outcome.Candidate.DecisionEpochSeconds - 60).DayOfWeek == DayOfWeek.Friday).ShouldBeGreaterThanOrEqualTo(5);
    }

    [Fact]
    public void The_reference_shares_no_code_with_the_frame_the_language_the_compiler_the_scanner_or_the_walker()
    {
        var folder = Path.GetDirectoryName(Here())!;
        var code = File.ReadAllLines(Path.Combine(folder, "..", "Support", "BaseBreakoutReference.cs")).Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)).ToList();
        var forbidden = new[]
        {
            "using XDrishti.Strategies", "using XDrishti.Indicators", "using XDrishti.Simulation.Scanning", "using XDrishti.Simulation.Walking", "using XDrishti.Simulation.Running", "using XDrishti.Simulation.Replay",
            "FeatureFrame", "StrategyCompiler", "EntryScanner", "TradeWalker", "SwingWalk", "Simulator.", "InstrumentReplayer", "BaseBreakoutV1",
        };

        forbidden.Where(f => code.Any(l => l.Contains(f, StringComparison.Ordinal))).ShouldBeEmpty();
    }

    private static string Here([CallerFilePath] string path = "") => path;
}
