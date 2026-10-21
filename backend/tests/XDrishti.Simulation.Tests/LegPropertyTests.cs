using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Tests.Oracle;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Simulation.Walking;

namespace XDrishti.Simulation.Tests;

/// <summary>
/// Criteria 4 to 6 of P4-01 as properties over 11,100 seeded series (O's generator at 600 series in each of its 16 categories, then the engine's intraday, daily and swing generators at 500 each, in the modelled fill mode), a buy and a
/// sell, intraday and swing, with the test-only rule (D14): the look-ahead property (D13), the stop that never loosens (D6) and the invariants of a trade with legs (D9). Each test counts what it reached, so that a property over nothing fails.
/// </summary>
public sealed class LegPropertyTests(ITestOutputHelper output)
{
    private const int PerCategory = 600;

    private static readonly decimal[] Shares = [0.5m, 0.25m, 0.4m];

    private static IEnumerable<ComparisonCase> Series() =>
        Enumerable.Range(1, OracleCategories.Count)
            .SelectMany(category => Enumerable.Range(0, PerCategory).Select(seed => ComparisonCases.OfOracle(OracleSeries.Of(category, seed), exact: false)))
            .Concat(ComparisonCases.Intraday(exact: false)).Concat(ComparisonCases.Daily(exact: false)).Concat(ComparisonCases.Swing(exact: false));

    private sealed record Walked(ComparisonCase Series, EntryCandidate Candidate, WalkRules Rules, decimal Share, Result<WalkOutcome> Outcome);

    private static IEnumerable<Walked> Walks(ComparisonCase series, bool askLoose)
    {
        var share = Shares[(int)(series.Seed % Shares.Length)];
        var built = LadderTape.Of(series.Bars, share, askLoose);
        foreach (var group in series.Groups)
        {
            foreach (var candidate in group.EngineCandidates)
            {
                yield return new Walked(series, candidate, group.EngineRules, share, TradeWalker.Walk(candidate, series.Bars, group.EngineRules, built.Context));
            }
        }
    }

    private sealed record Tally(int Series, int Trades, int WithLegs, int Buys, int Sells, int Swings, int Intraday, int Cuts, int Ignored)
    {
        public static Tally operator +(Tally a, Tally b) =>
            new(a.Series + b.Series, a.Trades + b.Trades, a.WithLegs + b.WithLegs, a.Buys + b.Buys, a.Sells + b.Sells, a.Swings + b.Swings, a.Intraday + b.Intraday, a.Cuts + b.Cuts, a.Ignored + b.Ignored);

        public static Tally Zero { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0);
    }

    private static Tally Count(Walked walked)
    {
        var trade = walked.Outcome.Value.Trade;
        if (trade?.Legs is null)
        {
            return Tally.Zero with { Trades = trade is null ? 0 : 1 };
        }

        var swing = walked.Rules.Hold is not null;
        return new Tally(0, 1, 1, trade.Side == Side.Buy ? 1 : 0, trade.Side == Side.Sell ? 1 : 0, swing ? 1 : 0, swing ? 0 : 1, 0, 0);
    }

    [Fact]
    public void Truncating_the_minutes_after_a_closed_bar_changes_nothing_the_position_did_at_or_before_the_first_minute_after_it()
    {
        var bad = new List<string>();
        var tally = Series().AsParallel().Select(series =>
        {
            var messages = new List<string>();
            var counts = LeakChecks(series, messages);
            lock (bad)
            {
                bad.AddRange(messages.Take(5));
            }

            return counts;
        }).Aggregate(Tally.Zero, (a, b) => a + b);

        output.WriteLine(tally.ToString());
        bad.Take(20).ShouldBeEmpty();
        tally.Series.ShouldBeGreaterThanOrEqualTo(10_000);
        new[] { tally.Buys, tally.Sells, tally.Swings, tally.Intraday }.ShouldAllBe(n => n > 0);
        tally.Cuts.ShouldBeGreaterThan(5_000);
    }

    private static Tally LeakChecks(ComparisonCase series, List<string> messages)
    {
        var total = new Tally(1, 0, 0, 0, 0, 0, 0, 0, 0);
        var share = Shares[(int)(series.Seed % Shares.Length)];
        foreach (var walked in Walks(series, askLoose: false))
        {
            total += Count(walked);
            if (walked.Outcome.Value.Trade is not { } full)
            {
                continue;
            }

            var tape = LadderTape.Of(series.Bars, share).Tape;
            var inside = Enumerable.Range(0, tape.Count).Where(k => tape.CloseEpoch(k) > full.EntryEpochSeconds && tape.CloseEpoch(k) <= full.ExitEpochSeconds + 3600).ToList();
            foreach (var k in new[] { 0, inside.Count / 2, inside.Count - 1 }.Where(i => i >= 0 && i < inside.Count).Distinct().Select(i => inside[i]))
            {
                var cut = tape.CloseEpoch(k);
                var kept = series.Bars.Where(b => b.EpochSeconds <= cut).ToList();
                if (kept.Count <= walked.Candidate.DecisionBarIndex + 1)
                {
                    continue;
                }

                total += new Tally(0, 0, 0, 0, 0, 0, 0, 1, 0);
                var truncated = TradeWalker.Walk(walked.Candidate, kept, walked.Rules, LadderTape.Of(kept, share).Context);
                var where = $"{series.Generator} {series.Seed} {walked.Candidate.Side} cut after bar {k} ({cut})";
                if (truncated.IsFailure || truncated.Value.Trade is not { } shorter)
                {
                    messages.Add(where + ": the truncated walk has no trade although the entry is before the cut");
                    continue;
                }

                var fullPartials = (full.Legs ?? new ValueList<Domain.Research.Leg>([])).Where(l => l.Kind == LegKind.Exit && l.Reason == TestLadderRule.Code && l.EpochSeconds <= cut).ToList();
                var shorterPartials = (shorter.Legs ?? new ValueList<Domain.Research.Leg>([])).Where(l => l.Kind == LegKind.Exit && l.Reason == TestLadderRule.Code && l.EpochSeconds <= cut).ToList();
                if (!fullPartials.SequenceEqual(shorterPartials))
                {
                    messages.Add(where + ": the partial exits at or before the cut differ");
                }

                if (full.ExitEpochSeconds <= cut && full.ExitReason is ExitReason.Stop or ExitReason.Target or ExitReason.SquareOff)
                {
                    if (truncated.Value != walked.Outcome.Value)
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

        return total;
    }

    private sealed class PeekingRule : IExitRule
    {
        public void OnBar(in ExitView view, ExitTape tape, int bar, List<ExitAction> actions)
        {
            // a leak: it looks at the next bar of the tape, which the minutes it may see have not closed yet
            if (view.ExitLegs == 0 && bar + 1 < tape.Count && tape.Close(bar + 1) > tape.Close(bar))
            {
                actions.Add(new ExitAction(ExitActionKind.ExitShare, 0.5m, "PEEK"));
            }
        }
    }

    [Fact]
    public void The_sentinel_a_rule_that_peeks_at_the_next_bar_gives_another_trade_when_the_minutes_after_the_bar_are_cut()
    {
        // 10:00 closes at 103.25, the next bar (10:15) at 103.50: the peeking rule books half at the first bar when the tape holds the next one, and nothing when the data ends with the first bar
        var day = Day.Dense().Start(100.50).Set("09:45 100.50/100.75/100.25/100.50", "09:59 102.00/103.25/102.00/103.25", "10:00 103.25/103.50/103.00/103.25", "10:20 103.25/103.50/103.00/103.25");
        var bars = day.Bars();
        var rules = new WalkRules(null, 915, Cand.NoSlippage, Cand.Tick05);
        var full = new ExitTape([TestLadderRule.LowFeature, TestLadderRule.HighFeature]);
        full.Append(DayBuilder.Epoch(DayBuilder.Monday, "10:00"), 103.25, [100.25, 103.25]);
        full.Append(DayBuilder.Epoch(DayBuilder.Monday, "10:15"), 103.50, [102.00, 103.50]);
        var cut = new ExitTape([TestLadderRule.LowFeature, TestLadderRule.HighFeature]);
        cut.Append(DayBuilder.Epoch(DayBuilder.Monday, "10:00"), 103.25, [100.25, 103.25]);

        var withFuture = TradeWalker.Walk(Cand.B(), bars, rules, new ExitContext(new PeekingRule(), full)).Value.Trade!;
        var withoutFuture = TradeWalker.Walk(Cand.B(), bars, rules, new ExitContext(new PeekingRule(), cut)).Value.Trade!;

        withFuture.Legs.ShouldNotBeNull().Count.ShouldBe(3, "the leak books a leg at 10:00 from a bar that closes at 10:15");
        withoutFuture.Legs.ShouldBeNull("with the data cut after the first bar the same rule books nothing: the comparison of the leak property would see this");
    }

    [Fact]
    public void A_rule_that_asks_for_a_looser_stop_changes_nothing_and_every_request_is_counted()
    {
        var bad = new List<string>();
        var ignored = 0L;
        var series = Series().ToList();
        var tallies = series.AsParallel().Select(one =>
        {
            var plain = Walks(one, askLoose: false).ToList();
            var asking = Walks(one, askLoose: true).ToList();
            var count = 0L;
            for (var i = 0; i < plain.Count; i++)
            {
                var without = plain[i].Outcome.Value;
                var with = asking[i].Outcome.Value;
                count += with.StopLoosenIgnored;
                if (with with { StopLoosenIgnored = 0 } != without)
                {
                    lock (bad)
                    {
                        bad.Add($"{one.Generator} {one.Seed} candidate {i}: a request to loosen the stop changed the walk");
                    }
                }

                if (without.StopLoosenIgnored != 0)
                {
                    lock (bad)
                    {
                        bad.Add($"{one.Generator} {one.Seed} candidate {i}: the plain rule never asks to loosen");
                    }
                }
            }

            return count;
        }).ToList();
        ignored = tallies.Sum();

        output.WriteLine($"series {series.Count}, ignored requests {ignored}");
        bad.Take(20).ShouldBeEmpty();
        series.Count.ShouldBeGreaterThanOrEqualTo(10_000);
        ignored.ShouldBeGreaterThan(10_000);
    }

    [Fact]
    public void The_legs_of_every_trade_add_up_ascend_stay_inside_their_minute_and_a_single_fill_each_way_carries_none()
    {
        var bad = new List<string>();
        var tally = Series().AsParallel().Select(series =>
        {
            var found = new List<string>();
            var total = new Tally(1, 0, 0, 0, 0, 0, 0, 0, 0);
            var byEpoch = series.Bars.ToDictionary(b => b.EpochSeconds);
            foreach (var walked in Walks(series, askLoose: false))
            {
                total += Count(walked);
                if (walked.Outcome.Value.Trade is not { } trade)
                {
                    continue;
                }

                var where = $"{series.Generator} {series.Seed} {trade.Side}";
                var legs = trade.Legs;
                if (legs is null)
                {
                    continue;
                }

                CheckLegs(trade, legs, byEpoch, walked.Share, where, found);
            }

            lock (bad)
            {
                bad.AddRange(found.Take(5));
            }

            return total;
        }).Aggregate(Tally.Zero, (a, b) => a + b);

        output.WriteLine(tally.ToString());
        bad.Take(20).ShouldBeEmpty();
        tally.Series.ShouldBeGreaterThanOrEqualTo(10_000);
        tally.WithLegs.ShouldBeGreaterThan(300);
        new[] { tally.Buys, tally.Sells, tally.Swings, tally.Intraday }.ShouldAllBe(n => n > 0);
    }

    private static void CheckLegs(SimTrade trade, ValueList<Domain.Research.Leg> legs, Dictionary<long, Bar> minutes, decimal share, string where, List<string> found)
    {
        var exits = legs.Where(l => l.Kind == LegKind.Exit).ToList();
        if (legs[0].Kind != LegKind.Entry || legs.Count(l => l.Kind == LegKind.Entry) != 1 || legs.Count < 3 || exits.Count < 2)
        {
            found.Add(where + ": a trade with legs has one entry and at least two exits");
        }

        if (exits.Sum(l => l.Share) != legs.Where(l => l.Kind != LegKind.Exit).Sum(l => l.Share))
        {
            found.Add(where + ": the shares out are not the shares in");
        }

        if (exits[0].Share != share)
        {
            found.Add(where + ": the first exit is not the share the rule asked for");
        }

        for (var i = 1; i < legs.Count; i++)
        {
            if (legs[i].EpochSeconds < legs[i - 1].EpochSeconds)
            {
                found.Add(where + $": leg {i} is earlier than the one before");
            }
        }

        if (legs[0].EpochSeconds != trade.EntryEpochSeconds || legs[^1].EpochSeconds != trade.ExitEpochSeconds || legs[0].Price != trade.EntryPrice || legs[^1].Price != trade.ExitPrice)
        {
            found.Add(where + ": the top-level entry and exit are not those of the first and the last leg");
        }

        foreach (var leg in legs)
        {
            // a stop or a target may be priced outside the range of its minute when the minute opened beyond it (ADR 0038); every other fill is a price of its minute
            var level = leg.Reason is nameof(ExitReason.Stop) or nameof(ExitReason.Target);
            if (!level && minutes.TryGetValue(leg.EpochSeconds, out var minute) && (leg.Price < minute.Low - 1e-9 || leg.Price > minute.High + 1e-9) && leg.Reason is not (nameof(ExitReason.EndOfData) or nameof(ExitReason.MaxHold)))
            {
                found.Add(where + $": a leg at {leg.EpochSeconds} is priced {leg.Price}, outside its minute {minute.Low} to {minute.High}");
            }

            if (leg.FilledPrice > leg.Price + 1e-9 && leg.Kind == LegKind.Exit && trade.Side == Side.Buy)
            {
                found.Add(where + ": an exit of a buy is filled above its price");
            }
        }
    }
}
