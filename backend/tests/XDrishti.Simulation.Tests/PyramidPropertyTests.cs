using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Tests.Oracle;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Simulation.Walking;

namespace XDrishti.Simulation.Tests;

/// <summary>
/// Criteria 3 and 7 of P4-04 as properties over 11,100 seeded series (O's generator at 600 series in each of its 16 categories, then the engine's intraday, daily and swing generators at 500 each, in the modelled fill mode), a buy and a sell,
/// intraday and swing, with the ladder, <c>max_adds</c>, multiple, full size and time stop of <see cref="PyramidVariant"/>: the look-ahead property (P4-00 D9: truncating the minutes after a closed bar changes nothing the position did at or before the
/// first minute after it, the legs in included), the stop that never loosens, the risk cap (after every add the open risk of all legs at the stop in force is at most 1 R), the legs (the invariants of P4-01 with the shares of the legs in at most
/// 100 %) and the claim of ADR 0052 that the risk rule never has to skip an add. A negative control, a rule that reads the next bar before it has closed, must fail the look-ahead property.
/// </summary>
public sealed class PyramidPropertyTests(ITestOutputHelper output)
{
    private const int PerCategory = 600;

    private const double Epsilon = 1e-9;

    private static IEnumerable<ComparisonCase> Series() =>
        Enumerable.Range(1, OracleCategories.Count)
            .SelectMany(category => Enumerable.Range(0, PerCategory).Select(seed => ComparisonCases.OfOracle(OracleSeries.Of(category, seed), exact: false)))
            .Concat(ComparisonCases.Intraday(exact: false)).Concat(ComparisonCases.Daily(exact: false)).Concat(ComparisonCases.Swing(exact: false));

    private static int Bucket(ComparisonCase series) =>
        series.Generator.Contains("aily", StringComparison.Ordinal) || (series.Generator.Contains("wing", StringComparison.Ordinal) && series.Seed % 2 == 1) ? 375 : 15;

    private sealed record Tally(int Series, int Trades, int Adds, int Buys, int Sells, int Swings, int Cuts, int Checks)
    {
        public static Tally operator +(Tally a, Tally b) => new(a.Series + b.Series, a.Trades + b.Trades, a.Adds + b.Adds, a.Buys + b.Buys, a.Sells + b.Sells, a.Swings + b.Swings, a.Cuts + b.Cuts, a.Checks + b.Checks);

        public static Tally Zero { get; } = new(0, 0, 0, 0, 0, 0, 0, 0);
    }

    private static Tally Count(EntryCandidate candidate, WalkRules rules, SimTrade? trade) =>
        trade is null ? Tally.Zero : new Tally(0, 1, trade.Legs?.Count(l => l.Kind == LegKind.Add) ?? 0, candidate.Side == Side.Buy ? 1 : 0, candidate.Side == Side.Sell ? 1 : 0, rules.Hold is not null ? 1 : 0, 0, 0);

    private static Result<WalkOutcome> Walk(EntryCandidate candidate, IReadOnlyList<Bar> minutes, WalkRules rules, int bucket, long seed, Func<ExitContext, IExitRule>? rule = null)
    {
        var variant = PyramidVariant.Of(seed, rules.Hold?.MaxHoldSessions ?? 0);
        var built = PyramidTape.Of(minutes, candidate.Side, variant, bucket);
        var context = rule is null ? built.Context : built.Context with { Rule = rule(built.Context) };
        return TradeWalker.Walk(candidate, minutes, rules with { RewardRisk = null }, context);
    }

    private static List<Leg> LegsIn(SimTrade trade, long upToEpoch) => trade.Legs is { } legs ? [.. legs.Where(l => l.Kind != LegKind.Exit && l.EpochSeconds <= upToEpoch)] : [];

    private static Tally LeakChecks(ComparisonCase series, List<string> messages, Func<ExitContext, IExitRule>? rule = null)
    {
        var total = new Tally(1, 0, 0, 0, 0, 0, 0, 0);
        var bucket = Bucket(series);
        foreach (var group in series.Groups)
        {
            foreach (var candidate in group.EngineCandidates)
            {
                var walked = Walk(candidate, series.Bars, group.EngineRules, bucket, series.Seed, rule);
                if (walked.IsFailure || walked.Value.Trade is not { } full)
                {
                    continue;
                }

                total += Count(candidate, group.EngineRules, full);
                var tape = PyramidTape.Of(series.Bars, candidate.Side, PyramidVariant.Of(series.Seed, group.EngineRules.Hold?.MaxHoldSessions ?? 0), bucket).Tape;
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
                    var truncated = Walk(candidate, kept, group.EngineRules, bucket, series.Seed, rule);
                    var where = $"{series.Generator} {series.Seed} {candidate.Side} cut after bar {k} ({cut})";
                    if (truncated.IsFailure || truncated.Value.Trade is not { } shorter)
                    {
                        messages.Add(where + ": the truncated walk has no trade although the entry is before the cut");
                        continue;
                    }

                    if (!LegsIn(full, cut).SequenceEqual(LegsIn(shorter, cut)))
                    {
                        messages.Add(where + ": the legs in up to the cut differ");
                    }

                    if (full.ExitEpochSeconds <= cut && full.ExitReason is ExitReason.Stop or ExitReason.SquareOff)
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
        new[] { tally.Buys, tally.Sells, tally.Swings, tally.Adds }.ShouldAllBe(n => n > 0);
        tally.Cuts.ShouldBeGreaterThan(5_000);
    }

    /// <summary>The negative control: moves the stop half a risk behind the close of the bar after the one it is given, a bar that closes later than the minute that is reading.</summary>
    private sealed class PeekingRule : IExitRule
    {
        public void OnBar(in ExitView view, ExitTape tape, int bar, List<ExitAction> actions)
        {
            var next = Math.Min(bar + 1, tape.Count - 1);
            if (TickGrid.TryConvert(tape.Close(next), out var close))
            {
                actions.Add(new ExitAction(ExitActionKind.MoveStop, close - ((view.IsBuy ? 1m : -1m) * view.Risk * 0.5m)));
            }
        }
    }

    [Fact]
    public void The_sentinel_a_rule_that_reads_a_bar_before_it_closes_fails_the_look_ahead_property_on_at_least_ten_series()
    {
        var found = new List<string>();
        foreach (var series in Series().Take(3000))
        {
            LeakChecks(series, found, _ => new PeekingRule());
        }

        found.Count.ShouldBeGreaterThanOrEqualTo(10, "the property must catch a rule that uses bar k before bar k has closed");
    }

    /// <summary>Records the stop in force and the share open each time the walker hands the rule a bar, then hands the bar on.</summary>
    private sealed class SpyRule(IExitRule inner, List<(decimal Stop, decimal Open, decimal Risk)> seen) : IExitRule
    {
        public void OnBar(in ExitView view, ExitTape tape, int bar, List<ExitAction> actions)
        {
            seen.Add((view.Stop, view.OpenShare, view.Risk));
            inner.OnBar(view, tape, bar, actions);
        }
    }

    [Fact]
    public void The_stop_never_loosens_the_open_risk_never_exceeds_one_R_and_the_legs_are_those_of_the_ladder()
    {
        var bad = new List<string>();
        var total = Series().AsParallel().Select(series =>
        {
            var tally = new Tally(1, 0, 0, 0, 0, 0, 0, 0);
            var bucket = Bucket(series);
            foreach (var group in series.Groups)
            {
                foreach (var candidate in group.EngineCandidates)
                {
                    var seen = new List<(decimal Stop, decimal Open, decimal Risk)>();
                    var variant = PyramidVariant.Of(series.Seed, group.EngineRules.Hold?.MaxHoldSessions ?? 0);
                    var walked = Walk(candidate, series.Bars, group.EngineRules, bucket, series.Seed, context => new SpyRule(context.Rule!, seen));
                    if (walked.IsFailure)
                    {
                        lock (bad)
                        {
                            bad.Add($"{series.Generator} {series.Seed}: {walked.Error.Message}");
                        }

                        continue;
                    }

                    var messages = new List<string>();
                    var checks = Check(candidate, walked.Value, variant, seen, messages);
                    tally += Count(candidate, group.EngineRules, walked.Value.Trade) + new Tally(0, 0, 0, 0, 0, 0, 0, checks);
                    if (messages.Count > 0)
                    {
                        lock (bad)
                        {
                            bad.AddRange(messages.Take(2).Select(m => $"{series.Generator} {series.Seed} {candidate.Side}: {m}"));
                        }
                    }
                }
            }

            return tally;
        }).Aggregate(Tally.Zero, (a, b) => a + b);

        output.WriteLine(total.ToString());
        bad.Take(20).ShouldBeEmpty();
        total.Series.ShouldBeGreaterThanOrEqualTo(10_000);
        total.Adds.ShouldBeGreaterThan(1_000);
        total.Checks.ShouldBeGreaterThan(10_000, "the walker handed the rule bars to read");
    }

    private static int Check(EntryCandidate candidate, WalkOutcome outcome, PyramidVariant variant, List<(decimal Stop, decimal Open, decimal Risk)> seen, List<string> messages)
    {
        var buy = candidate.Side == Side.Buy;
        var checks = 0;
        for (var i = 1; i < seen.Count; i++)
        {
            if (buy ? seen[i].Stop < seen[i - 1].Stop : seen[i].Stop > seen[i - 1].Stop)
            {
                messages.Add($"the stop went from {seen[i - 1].Stop} to {seen[i].Stop}");
            }
        }

        if (outcome.Trade is not { } trade)
        {
            return checks;
        }

        var legs = trade.Legs;
        if (legs is null || legs.Count < 2 || legs[0].Kind != LegKind.Entry || legs[^1].Kind != LegKind.Exit || legs.Count(l => l.Kind == LegKind.Exit) != 1)
        {
            messages.Add("a pyramid is written as an entry, adds and one exit leg");
            return checks;
        }

        var legsIn = legs.Where(l => l.Kind != LegKind.Exit).ToList();
        var adds = legsIn.Count - 1;
        if (legsIn.Skip(1).Any(l => l.Kind != LegKind.Add) || legsIn.Zip(legsIn.Skip(1), (a, b) => b.EpochSeconds <= a.EpochSeconds).Any(x => x))
        {
            messages.Add("the legs in are the entry, then adds in ascending order of instant");
        }

        if (legsIn[0].Share != variant.Initial || legsIn.Skip(1).Select(l => l.Share).SequenceEqual(variant.Shares.Take(adds)) is false && !PrefixOfSkips(legsIn.Skip(1).Select(l => l.Share).ToList(), variant))
        {
            messages.Add("the shares of the legs in are not those of the ladder");
        }

        var total = legsIn.Sum(l => l.Share);
        if (legs[^1].Share != total || total > 1m || adds > variant.MaxAdds)
        {
            messages.Add($"the exit leg takes {legs[^1].Share}, the legs in hold {total}, adds {adds} of at most {variant.MaxAdds}");
        }

        var counters = outcome.ToCounters();
        if (counters.AddsTaken != adds || counters.AddsSkippedRisk != 0 || counters.AddsTaken + counters.AddsMissed + counters.AddsSkippedQuantity > variant.MaxAdds)
        {
            messages.Add($"counters: taken {counters.AddsTaken} for {adds} add legs, skipped by risk {counters.AddsSkippedRisk}, missed {counters.AddsMissed}, skipped by quantity {counters.AddsSkippedQuantity}");
        }

        if (outcome.LegsTotal != legs.Count || trade.MaeR < 0 || trade.MfeR < 0 || trade.SlippageR > 0)
        {
            messages.Add("legs total, excursions or slippage out of range");
        }

        // The risk cap: whenever the rule is handed a bar, the legs filled so far (the prefix of the legs in whose shares add up to the share open) at the stop in force hold at most 1 R of open risk.
        foreach (var (stop, open, risk) in seen)
        {
            var share = 0m;
            var exposure = 0.0;
            var matched = false;
            foreach (var leg in legsIn)
            {
                share += leg.Share;
                exposure += (double)leg.Share * (buy ? leg.Price - (double)stop : (double)stop - leg.Price);
                if (share == open)
                {
                    matched = true;
                    break;
                }
            }

            if (!matched)
            {
                messages.Add($"no prefix of the legs in holds the open share {open}");
                break;
            }

            checks++;
            if (exposure / (double)risk > 1.0 + Epsilon)
            {
                messages.Add($"the open risk at the stop {stop} is {exposure / (double)risk} R");
            }
        }

        return checks;
    }

    /// <summary>The adds that filled are the ladder's shares in order with some levels missed or skipped: a subsequence of the shares.</summary>
    private static bool PrefixOfSkips(List<decimal> taken, PyramidVariant variant)
    {
        var next = 0;
        foreach (var share in taken)
        {
            while (next < variant.Shares.Length && variant.Shares[next] != share)
            {
                next++;
            }

            if (next >= variant.Shares.Length)
            {
                return false;
            }

            next++;
        }

        return true;
    }
}
