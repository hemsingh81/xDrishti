using XDrishti.Domain.Common;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Tests.Oracle;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Simulation.Walking;

namespace XDrishti.Simulation.Tests;

/// <summary>
/// Criterion 6 of P4-03 as properties over 11,100 seeded series (O's generator at 600 series in each of its 16 categories, then the engine's intraday, daily and swing generators at 500 each, in the modelled fill mode), a buy and a sell,
/// intraday and swing, for each of the three trails: the look-ahead property (P4-00 D9: truncating the minutes after a closed bar changes nothing the position did at or before the first minute after it), the stop that never loosens (D2, D4) and the
/// absence of legs (D9). A negative control, a rule that reads the value of a bar that has not closed, must fail the look-ahead property, so that a property over nothing cannot pass.
/// </summary>
public sealed class TrailPropertyTests(ITestOutputHelper output)
{
    private const int PerCategory = 600;

    private static readonly int[] Periods = [1, 3, 5, 14];

    private static readonly double[] Multiples = [0.5, 1.0, 2.0, 3.5];

    private static readonly double[] Activations = [0, 0, 0.5, 1.0, 2.0];

    public static TheoryData<OracleTrailKind> Kinds { get; } = [OracleTrailKind.Atr, OracleTrailKind.Structure, OracleTrailKind.Ema];

    private static IEnumerable<ComparisonCase> Series() =>
        Enumerable.Range(1, OracleCategories.Count)
            .SelectMany(category => Enumerable.Range(0, PerCategory).Select(seed => ComparisonCases.OfOracle(OracleSeries.Of(category, seed), exact: false)))
            .Concat(ComparisonCases.Intraday(exact: false)).Concat(ComparisonCases.Daily(exact: false)).Concat(ComparisonCases.Swing(exact: false));

    private sealed record Parameters(int Period, double Multiple, double Activation, int Bucket)
    {
        public static Parameters Of(ComparisonCase series) => new(
            Periods[(int)(series.Seed % Periods.Length)],
            Multiples[(int)((series.Seed / 3) % Multiples.Length)],
            Activations[(int)((series.Seed / 5) % Activations.Length)],
            series.Generator.Contains("aily", StringComparison.Ordinal) || (series.Generator.Contains("wing", StringComparison.Ordinal) && series.Seed % 2 == 1) ? 375 : 15);
    }

    private sealed record Tally(int Series, int Trades, int Buys, int Sells, int Swings, int Intraday, int Cuts, int Requests)
    {
        public static Tally operator +(Tally a, Tally b) =>
            new(a.Series + b.Series, a.Trades + b.Trades, a.Buys + b.Buys, a.Sells + b.Sells, a.Swings + b.Swings, a.Intraday + b.Intraday, a.Cuts + b.Cuts, a.Requests + b.Requests);

        public static Tally Zero { get; } = new(0, 0, 0, 0, 0, 0, 0, 0);
    }

    private static Tally Count(EntryCandidate candidate, WalkRules rules, SimTrade? trade) =>
        trade is null ? Tally.Zero : new Tally(0, 1, candidate.Side == Side.Buy ? 1 : 0, candidate.Side == Side.Sell ? 1 : 0, rules.Hold is not null ? 1 : 0, rules.Hold is null ? 1 : 0, 0, 0);

    /// <summary>Walks every candidate of the series alone with the rule of <paramref name="rule"/> over the tape built from <paramref name="minutes"/>.</summary>
    private static Result<WalkOutcome> Walk(EntryCandidate candidate, IReadOnlyList<Domain.Market.Bars.Bar> minutes, WalkRules rules, OracleTrailKind kind, Parameters p, Func<TrailTape.Built, IExitRule>? rule = null)
    {
        var built = TrailTape.Of(minutes, kind, candidate.Side, p.Period, p.Multiple, p.Activation, p.Bucket);
        return TradeWalker.Walk(candidate, minutes, rules with { RewardRisk = null }, rule is null ? built.Context : new ExitContext(rule(built), built.Tape));
    }

    private static Tally LeakChecks(ComparisonCase series, OracleTrailKind kind, List<string> messages, Func<TrailTape.Built, IExitRule>? rule = null)
    {
        var total = new Tally(1, 0, 0, 0, 0, 0, 0, 0);
        var p = Parameters.Of(series);
        foreach (var group in series.Groups)
        {
            foreach (var candidate in group.EngineCandidates)
            {
                var walked = Walk(candidate, series.Bars, group.EngineRules, kind, p, rule);
                if (walked.IsFailure || walked.Value.Trade is not { } full)
                {
                    continue;
                }

                total += Count(candidate, group.EngineRules, full);
                var tape = TrailTape.Of(series.Bars, kind, candidate.Side, p.Period, p.Multiple, p.Activation, p.Bucket).Tape;
                var inside = Enumerable.Range(0, tape.Count).Where(k => tape.CloseEpoch(k) > full.EntryEpochSeconds && tape.CloseEpoch(k) <= full.ExitEpochSeconds + 3600).ToList();
                foreach (var k in new[] { 0, inside.Count / 2, inside.Count - 1 }.Where(i => i >= 0 && i < inside.Count).Distinct().Select(i => inside[i]))
                {
                    var cut = tape.CloseEpoch(k);
                    var kept = series.Bars.Where(b => b.EpochSeconds <= cut).ToList();
                    if (kept.Count <= candidate.DecisionBarIndex + 1)
                    {
                        continue;
                    }

                    total += new Tally(0, 0, 0, 0, 0, 0, 1, 0);
                    var truncated = Walk(candidate, kept, group.EngineRules, kind, p, rule);
                    var where = $"{series.Generator} {series.Seed} {candidate.Side} cut after bar {k} ({cut})";
                    if (truncated.IsFailure || truncated.Value.Trade is not { } shorter)
                    {
                        messages.Add(where + ": the truncated walk has no trade although the entry is before the cut");
                        continue;
                    }

                    if (full.ExitEpochSeconds <= cut && full.ExitReason is ExitReason.Stop or ExitReason.SquareOff or ExitReason.EmaExit)
                    {
                        if (truncated.Value != walked.Value)
                        {
                            messages.Add(where + ": a trade that left before the cut is another trade");
                        }
                    }
                    else if (shorter.ExitReason != ExitReason.EndOfData && shorter.ExitEpochSeconds < full.ExitEpochSeconds)
                    {
                        messages.Add(where + $": the truncated trade left at {shorter.ExitEpochSeconds}, before the full one ({full.ExitEpochSeconds})");
                    }
                }
            }
        }

        return total;
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Truncating_the_minutes_after_a_closed_bar_changes_nothing_the_position_did_at_or_before_the_first_minute_after_it(OracleTrailKind kind)
    {
        var bad = new List<string>();
        var tally = Series().AsParallel().Select(series =>
        {
            var messages = new List<string>();
            var counts = LeakChecks(series, kind, messages);
            lock (bad)
            {
                bad.AddRange(messages.Take(5));
            }

            return counts;
        }).Aggregate(Tally.Zero, (a, b) => a + b);

        output.WriteLine($"{kind}: {tally}");
        bad.Take(20).ShouldBeEmpty();
        tally.Series.ShouldBeGreaterThanOrEqualTo(10_000);
        new[] { tally.Buys, tally.Sells, tally.Swings, tally.Intraday }.ShouldAllBe(n => n > 0);
        tally.Cuts.ShouldBeGreaterThan(5_000);
    }

    /// <summary>The negative control: reads the value of the bar after the one it is given, a bar that closes later than the minute that is reading, and moves the stop to it.</summary>
    private sealed class PeekingStructureRule : IExitRule
    {
        public void OnBar(in ExitView view, ExitTape tape, int bar, List<ExitAction> actions)
        {
            var next = Math.Min(bar + 1, tape.Count - 1);
            if (TickGrid.TryConvert(tape.Value(0, next), out var level))
            {
                actions.Add(new ExitAction(ExitActionKind.MoveStop, level));
            }
        }
    }

    [Fact]
    public void The_sentinel_a_rule_that_reads_a_bar_before_it_closes_fails_the_look_ahead_property()
    {
        var found = new List<string>();
        foreach (var series in Series().Take(3000))
        {
            LeakChecks(series, OracleTrailKind.Structure, found, _ => new PeekingStructureRule());
        }

        found.ShouldNotBeEmpty("the property must catch a rule that uses the value of bar k before bar k has closed");
    }

    /// <summary>Records the stop in force each time the walker hands the rule a bar, then hands the bar on.</summary>
    private sealed class SpyRule(IExitRule inner, List<decimal> stops) : IExitRule
    {
        public void OnBar(in ExitView view, ExitTape tape, int bar, List<ExitAction> actions)
        {
            stops.Add(view.Stop);
            inner.OnBar(view, tape, bar, actions);
        }
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void The_stop_never_loosens_and_a_trail_makes_no_leg(OracleTrailKind kind)
    {
        var bad = new List<string>();
        var total = Series().AsParallel().Select(series =>
        {
            var p = Parameters.Of(series);
            var tally = new Tally(1, 0, 0, 0, 0, 0, 0, 0);
            foreach (var group in series.Groups)
            {
                foreach (var candidate in group.EngineCandidates)
                {
                    var stops = new List<decimal>();
                    var walked = Walk(candidate, series.Bars, group.EngineRules, kind, p, built => new SpyRule(built.Context.Rule, stops));
                    if (walked.IsFailure)
                    {
                        lock (bad)
                        {
                            bad.Add($"{series.Generator} {series.Seed}: {walked.Error.Message}");
                        }

                        continue;
                    }

                    tally += Count(candidate, group.EngineRules, walked.Value.Trade) + new Tally(0, 0, 0, 0, 0, 0, 0, stops.Count);
                    var buy = candidate.Side == Side.Buy;
                    for (var i = 1; i < stops.Count; i++)
                    {
                        if (buy ? stops[i] < stops[i - 1] : stops[i] > stops[i - 1])
                        {
                            lock (bad)
                            {
                                bad.Add($"{series.Generator} {series.Seed} {candidate.Side}: the stop went from {stops[i - 1]} to {stops[i]}");
                            }
                        }
                    }

                    if (walked.Value.Trade is { } trade && (trade.Legs is not null || walked.Value.LegsTotal != 0))
                    {
                        lock (bad)
                        {
                            bad.Add($"{series.Generator} {series.Seed}: a trail made a leg");
                        }
                    }
                }
            }

            return tally;
        }).Aggregate(Tally.Zero, (a, b) => a + b);

        output.WriteLine($"{kind}: {total}");
        bad.Take(20).ShouldBeEmpty();
        total.Series.ShouldBeGreaterThanOrEqualTo(10_000);
        total.Trades.ShouldBeGreaterThan(10_000);
        total.Requests.ShouldBeGreaterThan(10_000, "the walker handed the rule bars to read");
    }
}
