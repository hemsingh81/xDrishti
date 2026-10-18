using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Running;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Simulation.Walking;

namespace XDrishti.Simulation.Tests;

/// <summary>
/// Criteria 21 and 23 of P3-03d: a closed trade does not depend on the future, over the 500 seeded multi-day series; the two negative controls (a walker that peeks at the next session, a calendar read off the list) fail the property on at
/// least ten series while the real walker fails on none; the same through the position gate, with the planted decisions after a due minute that is missing so that the guarantee of D11 is exercised and not assumed.
/// </summary>
public sealed class SwingFuturePropertyTests
{
    private const int AtLeast = 10;

    private delegate Result<WalkOutcome> WalkOnList(SwingSeries series, EntryCandidate candidate, IReadOnlyList<Bar> bars);

    private static readonly WalkOnList Honest = (s, c, bars) => TradeWalker.Walk(c, bars, s.Rules(FillRules.Default));

    private static readonly WalkOnList Peek = (s, c, bars) => TradeWalker.Walk(c, SwingWalks.PeekNextSession(bars), s.Rules(FillRules.Default));

    private static readonly WalkOnList CalendarFromTheFuture = (s, c, bars) => SwingWalks.WalkWithCalendarFromTheFuture(s, c, bars, FillRules.Default);

    /// <summary>
    /// The number of series on which an outcome settled at an index at most <c>k</c> is not identical, as rendered, when the minutes after <c>k</c> are removed or replaced by extreme values (epochs unchanged). The cuts are the settling
    /// index itself (the earliest cut that holds the whole outcome) and a random one after it.
    /// </summary>
    private static (int Series, int Compared) SeriesWithADifference(WalkOnList walk)
    {
        var different = 0;
        var compared = 0;
        foreach (var series in SeededSwing.All)
        {
            var differs = false;
            for (var index = 0; index < series.Candidates.Count; index++)
            {
                var candidate = series.Candidates[index];
                var full = walk(series, candidate, series.Bars);
                if (full.IsFailure)
                {
                    continue;   // a wrapper that cannot walk the full list has no outcome to compare
                }

                var settled = SwingWalks.SettledAt(series, candidate, series.Bars, full.Value);
                var rng = new SeededSeries(((ulong)series.Seed * 977UL) + (ulong)index);
                var cuts = new[] { settled, settled + rng.Int(series.Bars.Count - settled) };
                var expected = SwingWalks.Text(full);
                foreach (var k in cuts)
                {
                    var truncated = series.Bars.Take(k + 1).ToArray();
                    var replaced = WalkProperties.ReplaceAfter(series.Bars, k);
                    if (SwingWalks.Text(walk(series, candidate, truncated)) != expected || SwingWalks.Text(walk(series, candidate, replaced)) != expected)
                    {
                        differs = true;
                    }

                    compared++;
                }
            }

            different += differs ? 1 : 0;
        }

        return (different, compared);
    }

    [Fact]
    public void An_outcome_settled_at_or_before_a_cut_is_identical_when_the_minutes_after_it_are_removed_or_replaced_by_extreme_values()
    {
        var (different, compared) = SeriesWithADifference(Honest);

        different.ShouldBe(0, "the real walker is caught on no series");
        compared.ShouldBeGreaterThan(2_000);
    }

    [Fact]
    public void The_peek_next_session_control_is_caught_on_at_least_ten_series()
    {
        var (different, _) = SeriesWithADifference(Peek);

        TestContext.Current.TestOutputHelper?.WriteLine("peek-next-session: " + different + " of 500 series");
        different.ShouldBeGreaterThanOrEqualTo(AtLeast);
    }

    [Fact]
    public void The_calendar_from_the_future_control_is_caught_on_at_least_ten_series()
    {
        var (different, _) = SeriesWithADifference(CalendarFromTheFuture);

        TestContext.Current.TestOutputHelper?.WriteLine("calendar-from-the-future: " + different + " of 500 series");
        different.ShouldBeGreaterThanOrEqualTo(AtLeast);
    }

    [Fact]
    public void The_settling_index_of_every_outcome_is_the_minute_the_walk_had_to_read_and_never_before_the_exit()
    {
        foreach (var walk in SwingWalks.WalkAll(SeededSwing.All, FillRules.Default))
        {
            var settled = SwingWalks.SettledAt(walk.Series, walk.Candidate, walk.Series.Bars, walk.Outcome);

            settled.ShouldBeGreaterThanOrEqualTo(walk.Candidate.DecisionBarIndex, "seed " + walk.Series.Seed);
            if (walk.Trade is { } trade)
            {
                walk.Series.Bars[settled].EpochSeconds.ShouldBeGreaterThanOrEqualTo(trade.ExitEpochSeconds, "seed " + walk.Series.Seed);
            }
        }
    }

    // criterion 21c and 23: through the gate

    /// <summary>The hold end the gate uses: the exit instant, or the end of the due session for a MaxHold at the last close (the control uses the exit instant for both).</summary>
    private static long HoldEnd(SwingSeries series, WalkOutcome outcome, bool lastPrintControl)
    {
        var trade = outcome.Trade!;
        if (lastPrintControl || !outcome.MaxHoldAtLastClose)
        {
            return trade.ExitEpochSeconds;
        }

        var due = series.Due(trade.TradeDate);
        return Day.Epoch(due, series.DueMinute(due) + 1);
    }

    /// <summary>The plain loop of criterion 11: walk every candidate on its own, then apply <c>D ≤ E</c> and the end-of-data rule in decision order. Returns, for each candidate, whether it is blocked.</summary>
    private static List<bool> ReferenceGate(SwingSeries series, IReadOnlyList<Bar> bars, IReadOnlyList<EntryCandidate> candidates, bool lastPrintControl = false)
    {
        var rules = series.Rules(FillRules.Default);
        var alone = candidates.Select(c => TradeWalker.Walk(c, bars, rules).Value).ToList();
        var blocked = new List<bool>();
        long? end = null;
        var endedWithTheData = false;
        for (var i = 0; i < candidates.Count; i++)
        {
            var isBlocked = endedWithTheData || (end is { } e && candidates[i].DecisionEpochSeconds <= e);
            blocked.Add(isBlocked);
            if (!isBlocked && alone[i].Trade is { } trade)
            {
                endedWithTheData = trade.ExitReason == ExitReason.EndOfData;
                end = HoldEnd(series, alone[i], lastPrintControl);
            }
        }

        return blocked;
    }

    [Fact]
    public void The_gate_of_the_simulator_equals_the_reference_loop_over_the_500_series()
    {
        var blockedTotal = 0;
        var acceptedTotal = 0;
        foreach (var series in SeededSwing.All)
        {
            var gated = SwingWalks.Gated(series, FillRules.Default);
            var reference = ReferenceGate(series, series.Bars, series.Candidates);
            var rules = series.Rules(FillRules.Default);

            gated.Count.ShouldBe(reference.Count);
            for (var i = 0; i < gated.Count; i++)
            {
                (gated[i].Walk.NoTrade == NoTradeReason.PositionOpen).ShouldBe(reference[i], $"seed {series.Seed} candidate {i}");
                if (!reference[i])
                {
                    SwingWalks.Text(Result.Success(gated[i].Walk)).ShouldBe(SwingWalks.Text(TradeWalker.Walk(series.Candidates[i], series.Bars, rules)), $"seed {series.Seed} candidate {i}");
                    acceptedTotal++;
                }
                else
                {
                    blockedTotal++;
                }
            }
        }

        blockedTotal.ShouldBeGreaterThanOrEqualTo(AtLeast);
        acceptedTotal.ShouldBeGreaterThan(500);
    }

    [Fact]
    public void The_gate_decision_of_a_candidate_is_the_same_when_everything_after_its_decision_instant_is_removed()
    {
        var compared = 0;
        var plantedBlocked = 0;
        foreach (var series in SeededSwing.All)
        {
            var full = ReferenceGate(series, series.Bars, series.Candidates);
            for (var i = 0; i < series.Candidates.Count; i++)
            {
                var truncated = series.Bars.Take(series.Candidates[i].DecisionBarIndex + 1).ToArray();
                var prefix = ReferenceGate(series, truncated, [.. series.Candidates.Take(i + 1)]);

                prefix[i].ShouldBe(full[i], $"seed {series.Seed} candidate {i}");
                compared++;
                plantedBlocked += full[i] && series.Tags.Contains("due-minute-empty-with-candidates") ? 1 : 0;
            }
        }

        compared.ShouldBeGreaterThan(1_000);
        plantedBlocked.ShouldBeGreaterThanOrEqualTo(AtLeast, "the planted decisions after a due minute that is missing must be blocked in some series");
    }

    [Fact]
    public void The_same_through_the_gate_of_the_simulator_itself_not_only_the_reference_loop()
    {
        foreach (var series in SeededSwing.All)
        {
            var full = SwingWalks.Gated(series, FillRules.Default);
            for (var i = 0; i < series.Candidates.Count; i++)
            {
                var truncated = series.Bars.Take(series.Candidates[i].DecisionBarIndex + 1).ToArray();
                var rules = series.Rules(FillRules.Default);
                var result = PositionGate.Apply(series.Candidates.Take(i + 1), c => TradeWalker.Walk(c, truncated, rules));

                result.IsSuccess.ShouldBeTrue();
                (result.Value[i].Walk.NoTrade == NoTradeReason.PositionOpen).ShouldBe(full[i].Walk.NoTrade == NoTradeReason.PositionOpen, $"seed {series.Seed} candidate {i}");
            }
        }
    }

    [Fact]
    public void A_gate_that_ends_a_position_at_its_last_print_instead_of_the_end_of_its_due_session_is_caught_on_at_least_ten_series()
    {
        // The first draft's rule (review finding M2): E = the exit instant. A decision at 15:15 or 15:30 after a due minute that is missing is blocked in the short run (the position ended with the data) and walked in the long one.
        var different = 0;
        foreach (var series in SeededSwing.All)
        {
            var differs = false;
            var full = ReferenceGate(series, series.Bars, series.Candidates, lastPrintControl: true);
            for (var i = 0; i < series.Candidates.Count && !differs; i++)
            {
                var truncated = series.Bars.Take(series.Candidates[i].DecisionBarIndex + 1).ToArray();
                var prefix = ReferenceGate(series, truncated, [.. series.Candidates.Take(i + 1)], lastPrintControl: true);
                differs = prefix[i] != full[i];
            }

            different += differs ? 1 : 0;
        }

        TestContext.Current.TestOutputHelper?.WriteLine("gate with E = last print: " + different + " of 500 series");
        different.ShouldBeGreaterThanOrEqualTo(AtLeast);
    }

    [Fact]
    public void Accepted_trades_of_the_side_never_overlap_and_every_blocked_candidate_is_inside_a_position_or_follows_the_end_of_the_data()
    {
        var checkedBlocked = 0;
        foreach (var series in SeededSwing.All)
        {
            var outcomes = SwingWalks.Gated(series, FillRules.Default);
            SimTrade? last = null;
            long lastEnd = 0;
            foreach (var outcome in outcomes)
            {
                var candidate = outcome.Candidate;
                if (outcome.Walk.NoTrade == NoTradeReason.PositionOpen)
                {
                    last.ShouldNotBeNull($"seed {series.Seed}: nothing blocks the first candidate");
                    (last!.ExitReason == ExitReason.EndOfData || candidate.DecisionEpochSeconds <= lastEnd).ShouldBeTrue($"seed {series.Seed}: a blocked candidate is inside a position or follows the end of the data");
                    outcome.Walk.ToCounters().ShouldBe(WalkCounters.Empty with { Candidates = 1, PositionOpen = 1 });
                    checkedBlocked++;
                }
                else if (outcome.Walk.Trade is { } trade)
                {
                    if (last is not null)
                    {
                        candidate.DecisionEpochSeconds.ShouldBeGreaterThan(last.ExitEpochSeconds, $"seed {series.Seed}: accepted trades do not overlap (the next decision is after the start of the previous exit minute)");
                        candidate.DecisionEpochSeconds.ShouldBeGreaterThan(lastEnd, $"seed {series.Seed}");
                        last.ExitReason.ShouldNotBe(ExitReason.EndOfData, $"seed {series.Seed}: nothing is accepted after the end of the data");
                    }

                    last = trade;
                    lastEnd = outcome.Walk.HoldEndEpochSeconds;
                }
            }
        }

        checkedBlocked.ShouldBeGreaterThanOrEqualTo(AtLeast);
    }

    [Fact]
    public void A_blocked_candidate_is_not_walked_so_its_minutes_add_nothing_to_the_counters()
    {
        foreach (var series in SeededSwing.All)
        {
            var walked = new List<EntryCandidate>();
            var rules = series.Rules(FillRules.Default);

            var result = PositionGate.Apply(series.Candidates, c =>
            {
                walked.Add(c);
                return TradeWalker.Walk(c, series.Bars, rules);
            });

            var blocked = result.Value.Where(o => o.Walk.NoTrade == NoTradeReason.PositionOpen).Select(o => o.Candidate).ToList();
            walked.ShouldNotContain(c => blocked.Contains(c), "seed " + series.Seed);
            walked.Count.ShouldBe(series.Candidates.Count - blocked.Count, "seed " + series.Seed);
            result.Value.Where(o => o.Walk.NoTrade == NoTradeReason.PositionOpen).ShouldAllBe(o => o.Walk.BarsExamined == 0 && o.Walk.ZeroVolumeMinutes == 0);
        }
    }
}
