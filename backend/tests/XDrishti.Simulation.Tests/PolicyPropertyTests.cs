using XDrishti.Domain.Common;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Tests.Oracle;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Simulation.Walking;

namespace XDrishti.Simulation.Tests;

/// <summary>
/// Criteria 3 and 6 of P4-02 as properties over 11,100 seeded series for each family (O's generator at 600 series in each of its 16 categories, then the engine's intraday, daily and swing generators at 500 each, in the modelled fill
/// mode): the look-ahead property (P4-00 D9: truncating the data after bar <i>k</i> changes nothing at or before the first minute after <i>k</i>), with a negative control that makes each bar visible before it closes and must fail it;
/// the order inside the T1 minute (D2); and the invariants of a trade with a partial leg (the shares, R from the legs, T1 at the target, a trail that never loosens). Each test counts what it reached.
/// </summary>
public sealed class PolicyPropertyTests(ITestOutputHelper output)
{
    private const int PerCategory = 600;

    private static IEnumerable<ComparisonCase> Series() =>
        Enumerable.Range(1, OracleCategories.Count)
            .SelectMany(category => Enumerable.Range(0, PerCategory).Select(seed => ComparisonCases.OfOracle(OracleSeries.Of(category, seed), exact: false)))
            .Concat(ComparisonCases.Intraday(exact: false)).Concat(ComparisonCases.Daily(exact: false)).Concat(ComparisonCases.Swing(exact: false));

    private sealed record Walked(ComparisonCase Series, CaseGroup Group, EntryCandidate Candidate, PolicyVariant Variant, WalkRules Rules, Result<WalkOutcome> Outcome);

    private static IEnumerable<Walked> Walks(ComparisonCase series, PolicyFamily family, int leakEarlyBy = 0)
    {
        foreach (var group in series.Groups)
        {
            var variant = PolicyVariant.Of(family, series.Seed, group.Oracle.Rules.Hold?.MaxHoldSessions ?? 0);
            var rules = group.EngineRules with { RewardRisk = variant.RewardRisk(group.EngineRules.RewardRisk) };
            var built = PolicyTape.Of(series.Bars, variant, leakEarlyBy);
            foreach (var candidate in group.EngineCandidates)
            {
                yield return new Walked(series, group, candidate, variant, rules, TradeWalker.Walk(candidate, series.Bars, rules, built.Context));
            }
        }
    }

    private sealed record Tally(int Series, int Trades, int WithT1, int Stops, int Cuts, int Violations)
    {
        public static Tally operator +(Tally a, Tally b) => new(a.Series + b.Series, a.Trades + b.Trades, a.WithT1 + b.WithT1, a.Stops + b.Stops, a.Cuts + b.Cuts, a.Violations + b.Violations);

        public static Tally Zero { get; } = new(0, 0, 0, 0, 0, 0);
    }

    private static string Show(Leg leg) => $"{leg.EpochSeconds} {leg.Kind} {leg.Price:R} {leg.FilledPrice:R} {leg.Share} {leg.Reason}";

    private static Tally LeakChecks(ComparisonCase series, PolicyFamily family, int leakEarlyBy, List<string> messages)
    {
        var total = new Tally(1, 0, 0, 0, 0, 0);
        foreach (var walked in Walks(series, family, leakEarlyBy))
        {
            if (walked.Outcome.Value.Trade is not { } full)
            {
                continue;
            }

            total += new Tally(0, 1, 0, 0, 0, 0);
            var tape = PolicyTape.Of(series.Bars, walked.Variant, leakEarlyBy).Tape;
            var inside = Enumerable.Range(0, tape.Count).Where(k => tape.CloseEpoch(k) > full.EntryEpochSeconds && tape.CloseEpoch(k) <= full.ExitEpochSeconds + 3600).ToList();
            foreach (var k in new[] { 0, inside.Count / 2, inside.Count - 1 }.Where(i => i >= 0 && i < inside.Count).Distinct().Select(i => inside[i]))
            {
                var cut = tape.CloseEpoch(k);
                var kept = series.Bars.Where(b => b.EpochSeconds <= cut).ToList();
                if (kept.Count <= walked.Candidate.DecisionBarIndex + 1 || cut <= full.EntryEpochSeconds)
                {
                    continue;
                }

                var built = PolicyTape.Of(kept, walked.Variant, leakEarlyBy);
                var truncated = TradeWalker.Walk(walked.Candidate, kept, walked.Rules, built.Context);
                total += new Tally(0, 0, 0, 0, 1, 0);
                var where = $"{series.Generator} {series.Seed} {walked.Candidate.Side} cut after bar {k} ({cut})";
                if (truncated.IsFailure || truncated.Value.Trade is not { } shorter)
                {
                    messages.Add(where + ": the truncated walk has no trade although the entry is before the cut");
                    total += new Tally(0, 0, 0, 0, 0, 1);
                    continue;
                }

                // what the position did at or before the first minute after the bar: the legs of the two walks, without those that are made at the end of the data or at the end of a session that only a later minute shows
                var lastClose = walked.Outcome.Value.TimeStopAtLastClose || walked.Outcome.Value.MaxHoldAtLastClose;
                IEnumerable<Leg> Of(SimTrade trade, bool isFull) => (trade.Legs ?? LegsOf(trade)).Where(l => l.EpochSeconds <= cut && l.Reason != nameof(ExitReason.EndOfData) && !(isFull && lastClose && l.Reason is nameof(ExitReason.TimeStop) or nameof(ExitReason.MaxHold)));
                var fullLegs = Of(full, true).Select(Show).ToList();
                var shorterLegs = Of(shorter, false).Select(Show).ToList();
                if (!fullLegs.SequenceEqual(shorterLegs))
                {
                    messages.Add(where + $": the legs at or before the cut differ: [{string.Join("; ", fullLegs)}] against [{string.Join("; ", shorterLegs)}]");
                    total += new Tally(0, 0, 0, 0, 0, 1);
                }
            }
        }

        return total;
    }

    /// <summary>The entry and the one exit of a trade without legs, as legs.</summary>
    private static IReadOnlyList<Leg> LegsOf(SimTrade trade) =>
    [
        new Leg(trade.EntryEpochSeconds, trade.EntryPrice, trade.EntryFilledPrice, 1m, LegKind.Entry, string.Empty),
        new Leg(trade.ExitEpochSeconds, trade.ExitPrice, trade.ExitFilledPrice, 1m, LegKind.Exit, trade.ExitReason.ToString()),
    ];

    [Theory]
    [InlineData(PolicyFamily.Be)]
    [InlineData(PolicyFamily.Trail)]
    [InlineData(PolicyFamily.TimeStop)]
    public void Truncating_the_data_after_a_closed_bar_changes_nothing_the_position_did_at_or_before_the_first_minute_after_it(PolicyFamily family)
    {
        var bad = new List<string>();
        var tally = Series().AsParallel().Select(series =>
        {
            var messages = new List<string>();
            var counts = LeakChecks(series, family, 0, messages);
            lock (bad)
            {
                bad.AddRange(messages.Take(5));
            }

            return counts;
        }).Aggregate(Tally.Zero, (a, b) => a + b);

        output.WriteLine($"{family}: {tally}");
        bad.Take(10).ShouldBeEmpty();
        tally.Series.ShouldBeGreaterThanOrEqualTo(10_000);
        tally.Cuts.ShouldBeGreaterThan(5_000);
    }

    [Theory]
    [InlineData(PolicyFamily.Trail)]
    [InlineData(PolicyFamily.TimeStop)]
    public void The_negative_control_a_bar_visible_before_it_closes_fails_the_property(PolicyFamily family)
    {
        var bad = new List<string>();
        var tally = Series().Where((_, i) => i % 4 == 0).AsParallel().Select(series =>
        {
            var messages = new List<string>();
            var counts = LeakChecks(series, family, 60, messages);
            lock (bad)
            {
                bad.AddRange(messages.Take(5));
            }

            return counts;
        }).Aggregate(Tally.Zero, (a, b) => a + b);

        output.WriteLine($"{family} (leaky): {tally}");
        tally.Violations.ShouldBeGreaterThan(0, "a rule that sees a bar before it closes must be caught by the leak property");
    }

    [Fact]
    public void The_minute_that_fills_T1_is_never_also_a_break_even_stop_or_a_T2_fill_and_those_are_first_live_in_the_next_minute()
    {
        var tally = Series().AsParallel().Select(series =>
        {
            var seen = Tally.Zero;
            foreach (var walked in Walks(series, PolicyFamily.Be))
            {
                if (walked.Outcome.Value.Trade is not { Legs: { } legs } trade)
                {
                    continue;
                }

                var t1 = legs.Single(l => l.Reason == "T1");
                var after = legs.SkipWhile(l => l.Reason != "T1").Skip(1).First();
                seen += new Tally(0, 1, 1, 0, 0, 0);
                if (after.Reason is nameof(ExitReason.Stop) or nameof(ExitReason.Target))
                {
                    after.EpochSeconds.ShouldBeGreaterThan(t1.EpochSeconds, $"{series.Generator} {series.Seed}: the minute of T1 is also {after.Reason}");
                    seen += new Tally(0, 0, 0, after.Reason == nameof(ExitReason.Stop) ? 1 : 0, 1, 0);
                }

                // the break-even stop is the exact entry; T2 is above T1
                if (after.Reason == nameof(ExitReason.Stop) && !walked.Outcome.Value.StopGapExit)
                {
                    after.Price.ShouldBe(trade.EntryPrice, 1e-9);
                }
            }

            return seen;
        }).Aggregate(Tally.Zero, (a, b) => a + b);

        output.WriteLine(tally.ToString());
        tally.WithT1.ShouldBeGreaterThan(500);
        tally.Stops.ShouldBeGreaterThan(50);
        tally.Cuts.ShouldBeGreaterThan(100);
    }

    [Theory]
    [InlineData(PolicyFamily.Be)]
    [InlineData(PolicyFamily.Trail)]
    [InlineData(PolicyFamily.TimeStop)]
    public void The_invariants_of_a_trade_with_a_partial_leg_hold(PolicyFamily family)
    {
        var tally = Series().AsParallel().Select(series =>
        {
            var seen = Tally.Zero;
            foreach (var walked in Walks(series, family))
            {
                if (walked.Outcome.Value.Trade is not { } trade)
                {
                    continue;
                }

                seen += new Tally(0, 1, 0, 0, 0, 0);
                if (trade.Legs is not { } legs)
                {
                    continue;
                }

                var sign = trade.Side == Side.Buy ? 1.0 : -1.0;
                var risk = Math.Abs(trade.EntryPrice - trade.StopPrice);
                var exits = legs.Where(l => l.Kind == LegKind.Exit).ToList();
                seen += new Tally(0, 0, 1, 0, 0, 0);
                legs.Select(l => l.EpochSeconds).ShouldBe(legs.Select(l => l.EpochSeconds).Order(), "the legs ascend in time");
                exits.Sum(l => l.Share).ShouldBe(1m, "the shares out equal the shares in");
                exits.ShouldAllBe(l => l.Share > 0m);
                exits[0].Reason.ShouldBe("T1", "the first exit of a policy with a partial is T1");
                exits[0].Price.ShouldBe(trade.TargetPrice!.Value, 1e-9, "T1 is the target of the trade");
                (sign * (trade.TargetPrice!.Value - trade.EntryPrice) / risk).ShouldBe((double)walked.Variant.T1Rr, 1e-9);
                (exits.Sum(l => (double)l.Share * sign * (l.Price - trade.EntryPrice)) / risk).ShouldBe(trade.RExact, 1e-9, "R is the share-weighted sum over the legs");
                (exits.Sum(l => (double)l.Share * sign * (l.FilledPrice - trade.EntryFilledPrice)) / risk).ShouldBe(trade.RFilled, 1e-9);
                trade.SlippageR.ShouldBeLessThanOrEqualTo(1e-12);

                if (family == PolicyFamily.Trail && exits[^1].Reason == nameof(ExitReason.Stop) && !walked.Outcome.Value.StopGapExit)
                {
                    (sign * (exits[^1].Price - trade.StopPrice)).ShouldBeGreaterThanOrEqualTo(-1e-9, "a trail never loosens: the stop after T1 is not worse than the initial stop");
                    seen += new Tally(0, 0, 0, 1, 0, 0);
                }
            }

            return seen;
        }).Aggregate(Tally.Zero, (a, b) => a + b);

        output.WriteLine($"{family}: {tally}");
        tally.Trades.ShouldBeGreaterThan(5_000);
        if (family != PolicyFamily.TimeStop)
        {
            tally.WithT1.ShouldBeGreaterThan(500);
        }

        if (family == PolicyFamily.Trail)
        {
            tally.Stops.ShouldBeGreaterThan(50);
        }
    }
}
