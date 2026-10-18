using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Replay;
using XDrishti.Simulation.Running;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Simulation.Walking;

namespace XDrishti.Simulation.Tests.Replay;

/// <summary>
/// Criterion 18 of P3-03d (D14): window independence for multi-day trades, over 100 seeded multi-day instruments with flagged nights, for pairs of windows. <c>From</c>: the second window is the first with the trades dated before
/// it moved to the trades before the window, every other trade byte-identical and every position open the same. <c>To</c>: trades of the first run are trades of the second, open trades are still open or have finished or are
/// excluded across a flagged day, a candidate that was blocked stays blocked. Two negative controls, each of which must fail the property on at least ten of the 100 seeds: a scanner that starts at <c>From</c> and a gate that starts
/// empty at <c>From</c>; the real replay fails on none.
/// </summary>
public sealed class SwingWindowPropertyTests
{
    private const int AtLeast = 10;

    private static readonly PreparedStrategy Strategy = Rep.Prepare(TestStrategies.Swing1hEma(), Timeframe.H1, FillRules.Default);

    /// <summary>
    /// The windows of a series, chosen by its seed: two <c>From</c> dates and two <c>To</c> dates. The second <c>From</c> is, when the series has one, the date of a candidate that a position entered on an earlier date blocks (so that a
    /// gate that starts empty at <c>From</c> has something to get wrong); in seven series of ten the first <c>To</c> is a date at which a position is still open and the second is the next session, so that there is a position to follow.
    /// </summary>
    private static (DateOnly From1, DateOnly From2, DateOnly To1, DateOnly To2) Windows(SeededReplaySeries series)
    {
        var rng = new SeededSeries((ulong)series.Seed * 31UL);
        var n = series.Dates.Count;
        var a = rng.Int(n - 3);
        var b = a + 1 + rng.Int(n - 2 - a);
        if (StraddleDate(series) is { } straddle && series.Dates.IndexOf(straddle) > 0)
        {
            b = series.Dates.IndexOf(straddle);
            a = rng.Int(b);
        }

        var c = 3 + rng.Int(n - 5);
        var d = rng.Int(2) == 0 ? c + 1 : c + 1 + rng.Int(n - 1 - c);
        if (rng.Int(10) < 7 && OpenAtEndIndex(series) is { } open)
        {
            (c, d) = (open, open + 1);
        }
        return (series.Dates[a], series.Dates[b], series.Dates[c], series.Dates[d]);
    }

    /// <summary>The index of the first date (from the fourth session on, with a later one after it) at which a replay that ends there leaves a position open, or null.</summary>
    private static int? OpenAtEndIndex(SeededReplaySeries series)
    {
        for (var c = 3; c < series.Dates.Count - 1; c++)
        {
            if (Run(series, series.Dates[0], series.Dates[c]).Replay.OpenAtEnd.Count > 0)
            {
                return c;
            }
        }

        return null;
    }

    /// <summary>The date of the first candidate of the whole run that is blocked by a position entered on an earlier date, or null.</summary>
    private static DateOnly? StraddleDate(SeededReplaySeries series)
    {
        var (_, segments) = SeededReplay.Replay(Strategy, series.Bars, series.Dates[0], series.Dates[^1]);
        foreach (var segment in segments)
        {
            SimTrade? last = null;
            foreach (var outcome in segment.Result.Outcomes)
            {
                if (outcome.Walk.Trade is { } trade)
                {
                    last = trade;
                }
                else if (outcome.Walk.NoTrade == NoTradeReason.PositionOpen && last is not null)
                {
                    var date = SessionClock.DateOf(outcome.Candidate.DecisionEpochSeconds - 60);
                    if (last.TradeDate < date)
                    {
                        return date;
                    }
                }
            }
        }

        return null;
    }

    private static (InstrumentReplay Replay, List<SegmentRun> Segments) Run(SeededReplaySeries series, DateOnly from, DateOnly to) =>
        SeededReplay.Replay(Strategy, series.Through(to), from, to);

    private static string Text(SimTrade trade) => RunText.Trade(trade);

    private static SimTrade EntrySide(SimTrade trade) => trade with
    {
        ExitEpochSeconds = 0,
        ExitPrice = 0,
        ExitReason = ExitReason.Stop,
        RExact = 0,
        MaeR = 0,
        MfeR = 0,
        RFilled = 0,
        SlippageR = 0,
        ExitFilledPrice = 0,
        SessionsHeld = 0,
        SessionsUnseen = 0,
    };

    private static IEnumerable<CandidateOutcome> Outcomes(IEnumerable<SegmentRun> segments) => segments.SelectMany(s => s.Result.Outcomes);

    private static bool Differs(List<SimTrade> expected, IReadOnlyList<SimTrade> actual) =>
        expected.Count != actual.Count || expected.Zip(actual).Any(p => Text(p.First) != Text(p.Second));

    [Fact]
    public void A_later_from_moves_the_trades_dated_before_it_out_of_the_trades_and_changes_nothing_else()
    {
        var movedTotal = 0;
        foreach (var series in SeededReplay.All)
        {
            var (from1, from2, _, _) = Windows(series);
            var last = series.Dates[^1];
            var (first, _) = Run(series, from1, last);
            var (second, _) = Run(series, from2, last);
            var f1 = first.Record.Fates;
            var f2 = second.Record.Fates;
            var tradesBefore = first.Trades.Count(t => t.TradeDate < from2);
            var openBefore = first.OpenAtEnd.Count(t => t.TradeDate < from2);
            var label = $"seed {series.Seed} {from1:MM-dd} {from2:MM-dd}";

            Differs([.. first.Trades.Where(t => t.TradeDate >= from2)], second.Trades).ShouldBeFalse(label + ": trades");
            Differs([.. first.OpenAtEnd.Where(t => t.TradeDate >= from2)], second.OpenAtEnd).ShouldBeFalse(label + ": open at the end");
            f2.Trades.ShouldBe(f1.Trades - tradesBefore, label);
            f2.OpenAtEnd.ShouldBe(f1.OpenAtEnd - openBefore, label);
            (f2.NoFillBar, f2.AfterSquareOff, f2.EntryThroughStop, f2.LimitNotReached, f2.PositionOpen, f2.EntriesSuppressed).ShouldBe((f1.NoFillBar, f1.AfterSquareOff, f1.EntryThroughStop, f1.LimitNotReached, f1.PositionOpen, f1.EntriesSuppressed), label + ": the no-trades, position open included, are the same");
            (f2.TradesBeforeWindow - f1.TradesBeforeWindow).ShouldBe(tradesBefore + openBefore + (f1.ExcludedAcrossDiscontinuity - f2.ExcludedAcrossDiscontinuity), label + ": moved, not lost");
            f2.Total.ShouldBe(f1.Total, label);
            OutcomeFormatter.Render(second.Record.Walk).ShouldBe(OutcomeFormatter.Render(first.Record.Walk), label + ": the counters cover the replay, not the window");
            second.Record.Scans.Select(Render.Counters).ShouldBe(first.Record.Scans.Select(Render.Counters), label);
            movedTotal += tradesBefore + openBefore;
        }

        movedTotal.ShouldBeGreaterThanOrEqualTo(100, "the windows must move trades for this to compare anything");
    }

    [Fact]
    public void A_later_to_only_adds_sessions_trades_stay_open_trades_finish_and_a_blocked_candidate_stays_blocked()
    {
        var statuses = new Dictionary<string, int>();
        void Count(string key) => statuses[key] = statuses.GetValueOrDefault(key) + 1;
        var blockedChecked = 0;
        foreach (var series in SeededReplay.All)
        {
            var (_, _, to1, to2) = Windows(series);
            var from = series.Dates[0];
            var (first, segments1) = Run(series, from, to1);
            var (second, segments2) = Run(series, from, to2);
            var label = $"seed {series.Seed} to {to1:MM-dd} {to2:MM-dd}";
            var byEntry2 = second.Trades.Concat(second.OpenAtEnd).ToDictionary(t => (t.EntryEpochSeconds, t.Side));

            // (a) every trade of the first run is a trade of the second, byte-identical.
            foreach (var trade in first.Trades)
            {
                second.Trades.ShouldContain(t => Text(t) == Text(trade), label + ": a closed trade stays");
            }

            // (b) every open trade of the first run is, in the second, a trade or still open (with the same entry and an exit not before), or was excluded across a flagged day.
            var excludedNow = 0;
            foreach (var open in first.OpenAtEnd)
            {
                if (!byEntry2.TryGetValue((open.EntryEpochSeconds, open.Side), out var later))
                {
                    excludedNow++;
                    Count("open->excluded");
                    continue;
                }

                EntrySide(later).ShouldBe(EntrySide(open), label + ": same entry-side fields");
                later.ExitEpochSeconds.ShouldBeGreaterThanOrEqualTo(open.ExitEpochSeconds, label);
                Count(later.ExitReason == ExitReason.EndOfData ? "open->open" : "open->trade");
            }

            excludedNow.ShouldBeLessThanOrEqualTo((int)(second.Record.Fates.ExcludedAcrossDiscontinuity - first.Record.Fates.ExcludedAcrossDiscontinuity), label + ": an open trade that vanished is accounted for as excluded");

            // (c) a trade of the second run dated up to To1 is, in the first, a trade or open at the end.
            var byEntry1 = first.Trades.Concat(first.OpenAtEnd).Select(t => (t.EntryEpochSeconds, t.Side)).ToHashSet();
            foreach (var trade in second.Trades.Concat(second.OpenAtEnd).Where(t => t.TradeDate <= to1))
            {
                byEntry1.ShouldContain((trade.EntryEpochSeconds, trade.Side), label + ": a trade dated in the first window was seen by the first run");
            }

            // (d) a candidate that is a position open in the first run is a position open in the second.
            var second2 = Outcomes(segments2).ToDictionary(o => (o.Candidate.DecisionEpochSeconds, o.Candidate.Side));
            foreach (var blocked in Outcomes(segments1).Where(o => o.Walk.NoTrade == NoTradeReason.PositionOpen))
            {
                second2[(blocked.Candidate.DecisionEpochSeconds, blocked.Candidate.Side)].Walk.NoTrade.ShouldBe(NoTradeReason.PositionOpen, label + ": a blocked candidate stays blocked");
                blockedChecked++;
            }
        }

        TestContext.Current.TestOutputHelper?.WriteLine(string.Join(", ", statuses.OrderBy(s => s.Key).Select(s => s.Key + "=" + s.Value)) + $", blocked candidates compared={blockedChecked}");
        statuses.GetValueOrDefault("open->trade").ShouldBeGreaterThanOrEqualTo(AtLeast, "trades that were open and finished");
        statuses.GetValueOrDefault("open->open").ShouldBeGreaterThanOrEqualTo(AtLeast, "trades that were open and still are");
        statuses.GetValueOrDefault("open->excluded").ShouldBeGreaterThanOrEqualTo(1, "a flagged day between the two ends");
        blockedChecked.ShouldBeGreaterThanOrEqualTo(AtLeast);
    }

    [Fact]
    public void The_series_hold_flagged_nights_trades_open_at_the_end_and_candidates_that_are_blocked()
    {
        SeededReplay.All.Count(s => s.FlaggedDates.Count > 0).ShouldBeGreaterThanOrEqualTo(AtLeast);
        var runs = SeededReplay.All.Select(s => Run(s, s.Dates[0], s.Dates[^1]).Replay.Record).ToList();

        runs.Sum(r => r.Fates.Trades).ShouldBeGreaterThan(100);
        runs.Sum(r => r.Fates.PositionOpen).ShouldBeGreaterThanOrEqualTo(AtLeast);
        runs.Sum(r => r.Fates.ExcludedAcrossDiscontinuity).ShouldBeGreaterThanOrEqualTo(AtLeast);
        runs.Sum(r => r.Fates.OpenAtEnd).ShouldBeGreaterThanOrEqualTo(AtLeast);
        ((long)runs.Sum(r => r.Discontinuities.Sum(d => d.ExcludedPositions))).ShouldBe(runs.Sum(r => r.Fates.ExcludedAcrossDiscontinuity));
    }

    [Fact]
    public void Control_a_replay_whose_scanner_starts_at_from_loses_the_history_and_fails_the_property_on_at_least_ten_series()
    {
        var different = 0;
        foreach (var series in SeededReplay.All)
        {
            var (from1, from2, _, _) = Windows(series);
            var last = series.Dates[^1];
            var expected = Run(series, from1, last).Replay.Trades.Where(t => t.TradeDate >= from2).ToList();

            // The design that P3-04 rejected: the bars start at From, so ema(10) is seeded by the first bar of the window.
            var control = SeededReplay.Replay(Strategy, series.From(from2), from2, last).Replay.Trades;

            different += Differs(expected, control) ? 1 : 0;
        }

        TestContext.Current.TestOutputHelper?.WriteLine("scanner starts at From: " + different + " of 100 series");
        different.ShouldBeGreaterThanOrEqualTo(AtLeast);
    }

    [Fact]
    public void Control_a_gate_that_starts_empty_at_from_walks_candidates_that_a_position_from_before_from_blocks_and_fails_on_at_least_ten_series()
    {
        var different = 0;
        var rules = new WalkRules(2.0, WalkRules.NoSquareOff, FillRules.Default, Sim.Tick, SwingHold.Create(3, TestCalendars.DefaultWeek()).Value);
        foreach (var series in SeededReplay.All)
        {
            var (_, from2, _, _) = Windows(series);
            var last = series.Dates[^1];
            var (_, segments) = Run(series, series.Dates[0], last);
            var differs = false;
            foreach (var segment in segments)
            {
                var real = segment.Result.Outcomes.Where(o => SessionClock.DateOf(o.Candidate.DecisionEpochSeconds - 60) >= from2).ToList();
                var reset = PositionGate.Apply(real.Select(o => o.Candidate), c => TradeWalker.Walk(c, segment.List, rules)).Value;

                differs |= real.Zip(reset).Any(p => (p.First.Walk.NoTrade == NoTradeReason.PositionOpen) != (p.Second.Walk.NoTrade == NoTradeReason.PositionOpen));
            }

            different += differs ? 1 : 0;
        }

        TestContext.Current.TestOutputHelper?.WriteLine("gate starts empty at From: " + different + " of 100 series");
        different.ShouldBeGreaterThanOrEqualTo(AtLeast);
    }
}
