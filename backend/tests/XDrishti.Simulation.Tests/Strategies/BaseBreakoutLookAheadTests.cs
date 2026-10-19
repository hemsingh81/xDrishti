using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Scanning;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Simulation.Walking;
using XDrishti.Strategies.BuiltIn;
using XDrishti.Strategies.Compilation;

namespace XDrishti.Simulation.Tests.Strategies;

/// <summary>
/// Criterion 12 of P3-06, the look-ahead property of P3-03a for <c>BASE_BREAKOUT</c> on 200 generated series (<see cref="SeededBase"/>, with the missing 15:29 minute on 15 % of the sessions): for a cut <c>k</c> (a random session end and the decision bar of the
/// first candidate and the bar before it) the candidates with <c>DecisionBarIndex ≤ k</c>, trace included, and the counters as of step <c>k</c> are equal to those of the full run when the bars after <c>k</c> are removed and when they are replaced by extreme values
/// (zero and huge prices and volumes). The negative control is the peeking feed of P3-03a (bar <i>i</i> carries the content of bar <i>i</i>+1), reported on at least 10 seeds while the honest run reports none on the same inputs. For the walker the entry of
/// every candidate is settled by the minutes up to the fill minute (one more for a limit that is not reached); the control hands the walker a stop that uses the lowest low of the fill session in the list it is given.
/// </summary>
public sealed class BaseBreakoutLookAheadTests
{
    private const int Count = 200;

    private static readonly BaseSeries[] All = [.. Enumerable.Range(1, Count).Select(SeededBase.Make)];

    private sealed record Snapshot(string[] Candidates, string Counters);

    private static CompiledSide Compiled(BaseSeries s) =>
        Scan.Compile(BaseBreakoutV1.Create(s.BaseLen, (double)s.BasePct, (double)s.VolX, (double)s.RewardRisk), Side.Buy, Timeframe.D1);

    private static Snapshot Capture(EntryScanner scanner, int k) => new(
        [.. scanner.Candidates.Where(c => c.DecisionBarIndex <= k).Select(c => Render.Candidate(c))],
        Render.Counters(scanner.Counters));

    private static Snapshot RunTo(CompiledSide side, TradingCalendar calendar, Bar[] bars, int k)
    {
        var scanner = Scan.Scanner(side, calendar);
        Snapshot? snapshot = null;
        for (var i = 0; i < bars.Length; i++)
        {
            scanner.Step(bars[i]);
            if (i == k)
            {
                snapshot = Capture(scanner, k);
            }
        }

        return snapshot ?? throw new InvalidOperationException("The cut is outside the list.");
    }

    private static List<int> SessionEnds(Bar[] bars)
    {
        var ends = new List<int>();
        for (var i = 0; i < bars.Length; i++)
        {
            if (i + 1 == bars.Length || SessionClock.DateOf(bars[i + 1].EpochSeconds) != SessionClock.DateOf(bars[i].EpochSeconds))
            {
                ends.Add(i);
            }
        }

        return ends;
    }

    private static List<int> Cuts(BaseSeries series, Bar[] bars, IReadOnlyList<EntryCandidate> candidates)
    {
        var rng = new SeededSeries((ulong)series.Seed * 7919);
        var ends = SessionEnds(bars);
        var cuts = new List<int> { ends[10 + rng.Int(ends.Count - 12)] };
        if (candidates.Count > 0)
        {
            var at = candidates[rng.Int(candidates.Count)].DecisionBarIndex;
            cuts.Add(at);
            cuts.Add(at - 1);
        }

        return cuts;
    }

    private static bool Same(Snapshot a, Snapshot b) => a.Counters == b.Counters && a.Candidates.SequenceEqual(b.Candidates);

    private static bool Violated(BaseSeries series, Func<Bar[], Bar[]> feed)
    {
        var side = Compiled(series);
        var bars = series.Bars(withMissingClose: true);
        var fullFeed = feed(bars);
        var full = Scan.Run(side, fullFeed, series.Calendar);
        var violated = false;
        foreach (var k in Cuts(series, bars, full.Candidates))
        {
            var expected = RunTo(side, series.Calendar, fullFeed, k);
            var removed = RunTo(side, series.Calendar, feed([.. bars.Take(k + 1)]), k);
            var replaced = RunTo(side, series.Calendar, feed(Feeds.Replaced(bars, k, (ulong)series.Seed)), k);

            violated |= !Same(expected, removed) || !Same(expected, replaced);
        }

        return violated;
    }

    [Fact]
    public void The_generator_makes_enough_candidates_and_stale_bars_for_the_property_to_mean_something()
    {
        var scans = All.Select(s => Scan.Run(Compiled(s), s.Bars(withMissingClose: true), s.Calendar).Counters).ToList();

        var candidates = scans.Sum(c => c.Candidates);
        var stale = scans.Sum(c => c.StaleBucketSteps);
        candidates.ShouldBeGreaterThanOrEqualTo(60, "candidates " + candidates);
        stale.ShouldBeGreaterThanOrEqualTo(100, "stale daily bars " + stale);
        scans.Sum(c => c.LaterTriggers).ShouldBe(0, "a daily strategy has one decision a day");
        scans.ShouldAllBe(c => c.DecisionSteps == c.StaleBucketSteps + c.OutOfWindowSteps + c.WindowSteps);
    }

    [Fact]
    public void The_honest_scanner_reports_no_violation_on_any_of_the_200_series()
    {
        All.Where(s => Violated(s, bars => bars)).Select(s => s.Seed).ShouldBeEmpty();
    }

    [Fact]
    public void A_cut_at_the_bar_of_a_candidate_keeps_it_and_a_cut_one_bar_before_removes_it()
    {
        var kept = 0;
        foreach (var series in All)
        {
            var side = Compiled(series);
            var bars = series.Bars(withMissingClose: true);
            var full = Scan.Run(side, bars, series.Calendar);
            if (full.Candidates.Count == 0)
            {
                continue;
            }

            var at = full.Candidates[0].DecisionBarIndex;
            RunTo(side, series.Calendar, [.. bars.Take(at + 1)], at).Candidates.Length.ShouldBeGreaterThan(0, "seed " + series.Seed);
            RunTo(side, series.Calendar, [.. bars.Take(at)], at - 1).Candidates.ShouldBeEmpty("seed " + series.Seed);
            kept++;
        }

        kept.ShouldBeGreaterThanOrEqualTo(20, "series with a candidate " + kept);
    }

    [Fact]
    public void The_peeking_feed_is_reported_on_at_least_ten_seeds_while_the_honest_run_reports_none_on_the_same_inputs()
    {
        var reported = All.Count(s => Violated(s, Feeds.Peeking));
        var honest = All.Count(s => Violated(s, bars => bars));

        honest.ShouldBe(0);
        reported.ShouldBeGreaterThanOrEqualTo(10, "seeds reported " + reported);
    }

    // ---- the walker: the entry is settled by the minutes up to the fill minute

    private static WalkRules Rules(BaseSeries s) =>
        new((double)s.RewardRisk, WalkRules.NoSquareOff, Cand.NoSlippage, Sim.Tick, SwingHold.Create(5, s.Calendar).Value);

    private static string Entry(WalkOutcome o) => o.Trade is { } t
        ? $"trade {t.TradeDate:yyyy-MM-dd} {t.EntryEpochSeconds} {t.EntryPrice:R} {t.EntryFilledPrice:R} {t.EntryFill} {t.StopPrice:R} {t.TargetPrice:R}"
        : $"no-trade {o.NoTrade}";

    private static WalkOutcome Plain(BaseSeries series, IReadOnlyList<Bar> bars, EntryCandidate candidate)
    {
        var result = TradeWalker.Walk(candidate, bars, Rules(series));
        result.IsSuccess.ShouldBeTrue();
        return result.Value;
    }

    /// <summary>The control: the stop handed to the walker is the higher of the candidate's and the lowest low of the fill session in the list it is given (a look-ahead).</summary>
    private static WalkOutcome StopFromTheFillSessionsLow(BaseSeries series, IReadOnlyList<Bar> bars, EntryCandidate candidate)
    {
        var decisionDate = SessionClock.DateOf(candidate.DecisionEpochSeconds - 60);
        var low = bars.Where(b => SessionClock.DateOf(b.EpochSeconds) > decisionDate).Take(4).Select(b => b.Low).DefaultIfEmpty(candidate.StopLevel).Min();
        return Plain(series, bars, candidate with { StopLevel = Math.Max(candidate.StopLevel, low) });
    }

    private static Bar[] Extreme(IReadOnlyList<Bar> bars, int after)
    {
        var copy = bars.ToArray();
        for (var j = after + 1; j < copy.Length; j++)
        {
            copy[j] = copy[j] with { Open = 1e7 + j, High = 2e7 + j, Low = 1e-3, Close = 1e7 + j };
        }

        return copy;
    }

    private static (int Failed, int Compared) WalkerLookAhead(Func<BaseSeries, IReadOnlyList<Bar>, EntryCandidate, WalkOutcome> walk)
    {
        var failed = 0;
        var compared = 0;
        foreach (var series in All)
        {
            var bars = series.Bars();
            var seriesFailed = false;
            foreach (var outcome in BaseRun.Of(series).Outcomes)
            {
                var candidate = outcome.Candidate;
                var decisionDate = SessionClock.DateOf(candidate.DecisionEpochSeconds - 60);
                var fillMinute = bars.ToList().FindIndex(b => SessionClock.DateOf(b.EpochSeconds) > decisionDate);
                if (fillMinute < 0 || outcome.Walk.NoTrade == NoTradeReason.PositionOpen)
                {
                    continue;
                }

                var k = outcome.Walk.NoTrade == NoTradeReason.LimitNotReached ? fillMinute + 1 : fillMinute;
                var full = Entry(walk(series, bars, candidate));
                var cut = Entry(walk(series, [.. bars.Take(k + 1)], candidate));
                var extreme = Entry(walk(series, Extreme(bars, k), candidate));
                compared++;
                seriesFailed |= full != cut || full != extreme;
            }

            if (seriesFailed)
            {
                failed++;
            }
        }

        return (failed, compared);
    }

    [Fact]
    public void The_entry_of_an_overnight_order_is_settled_by_the_minutes_up_to_the_fill_minute_and_by_nothing_after_it_over_200_series()
    {
        var (failed, compared) = WalkerLookAhead(Plain);

        failed.ShouldBe(0);
        compared.ShouldBeGreaterThanOrEqualTo(40, "settled candidates " + compared);
    }

    [Fact]
    public void Control_a_stop_taken_from_the_low_of_the_fill_session_fails_on_at_least_ten_series()
    {
        var (failed, _) = WalkerLookAhead(StopFromTheFillSessionsLow);

        failed.ShouldBeGreaterThanOrEqualTo(10, "series reported " + failed);
    }
}
