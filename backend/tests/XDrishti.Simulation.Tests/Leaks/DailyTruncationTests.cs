using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Running;
using XDrishti.Simulation.Tests.Support;

namespace XDrishti.Simulation.Tests.Leaks;

/// <summary>
/// Truncation of a daily-evaluated swing strategy through <c>Simulator.Run</c> (spec P3-08b, Annex B (e); ADR 0043: the decision of a daily bar is taken at 15:30 and the order rests overnight). <c>OvernightEntryTests</c> proves at the walker and the gate that the
/// resting order is settled by the minutes up to its fill minute; this proves it through the loop (the scanner, the gate, the walker) over the 500 seeded daily series. For a cut after session c (a seeded one, the session of the first candidate and the session
/// before it), the series that ends at c and the full series give: (1) the same candidates with a decision at or before the end of the cut, trace included; (2) for the leading outcomes that are <i>final</i> in the prefix, the same outcome, field by field.
/// An outcome is not final when its trade is cut by the end of the data (<c>EndOfData</c>), when its order is still resting (<c>OrderPendingAtEnd</c>) or when no fill minute has been seen (<c>NoFillBar</c>); and the position gate (ADR 0040 D11) makes every
/// later outcome depend on the open one, so the comparison stops at the first outcome that is not final. The existing controls are kept (the peeking feed and the peeking extremes of the property of P3-03a); one is added for the overnight order: each minute's range is
/// widened by the next session's, a walker input that reads the fill session.
/// </summary>
public sealed class DailyTruncationTests(ITestOutputHelper output)
{
    private static readonly Dictionary<int, Simulator> Simulators = Enumerable.Range(1, 5).ToDictionary(h => h, h => Sim.Create(TestStrategies.SwingDaily(DailyFixture.Trigger, h), Timeframe.D1, FillRules.Default));

    private static bool Final(CandidateOutcome o) =>
        o.Walk.Trade is { } t ? t.ExitReason != ExitReason.EndOfData : o.Walk.NoTrade is not (NoTradeReason.OrderPendingAtEnd or NoTradeReason.NoFillBar);

    private static SimulationResult Run(DailySeries s, IReadOnlyList<Bar> bars) => Sim.Run(Simulators[s.Sessions], bars, tick: Leak.Tick(s.Tick), calendar: s.Truth);

    /// <summary>The cuts of a series in sessions kept (1 to n-1): a seeded one, the session of the first candidate of the full run and the session before it.</summary>
    private static int[] Cuts(DailySeries s, SimulationResult full)
    {
        var rng = new SeededSeries((ulong)s.Seed * 7_907UL);
        var cuts = new List<int> { 1 + rng.Int(s.Dates.Count - 1) };
        if (full.Outcomes.Count > 0)
        {
            var date = SessionClock.DateOf(full.Outcomes[0].Candidate.DecisionEpochSeconds - 60);
            var at = s.Dates.ToList().IndexOf(date) + 1;
            cuts.Add(at);
            cuts.Add(at - 1);
        }

        return [.. cuts.Where(c => c >= 1 && c < s.Dates.Count).Distinct()];
    }

    /// <summary>Whether the property is violated for the series with the inputs transformed by <paramref name="feed"/>, and how much was compared.</summary>
    private static (bool Violated, int Cuts, int Candidates, int Final, int Pending) Check(DailySeries s, Func<Bar[], Bar[]> feed)
    {
        var fullFeed = feed([.. s.Bars]);
        var full = Run(s, fullFeed);
        var violated = false;
        int cuts = 0, candidates = 0, final = 0, pending = 0;
        foreach (var c in Cuts(s, Run(s, s.Bars)))
        {
            var prefixFeed = feed([.. s.Prefix(c)]);
            var prefix = Run(s, prefixFeed);
            var k = prefixFeed.Length - 1;
            cuts++;

            var expected = full.Outcomes.Where(o => o.Candidate.DecisionBarIndex <= k).Select(o => Leak.CandidateLine(o)).ToList();
            var actual = prefix.Outcomes.Select(o => Leak.CandidateLine(o)).ToList();
            violated |= !expected.SequenceEqual(actual);
            candidates += actual.Count;
            pending += prefix.Outcomes.Count(o => o.Walk.NoTrade == NoTradeReason.OrderPendingAtEnd);

            foreach (var outcome in prefix.Outcomes.TakeWhile(Final))
            {
                final++;
                var same = full.Outcomes.FirstOrDefault(o => o.Candidate.DecisionBarIndex == outcome.Candidate.DecisionBarIndex && o.Candidate.Side == outcome.Candidate.Side);
                violated |= same is null || Leak.Line(same) != Leak.Line(outcome);
            }
        }

        return (violated, cuts, candidates, final, pending);
    }

    /// <summary>The control that reads the fill session: the range of every minute is widened by the extremes of the next session.</summary>
    private static Bar[] NextSessionExtremes(Bar[] bars)
    {
        var dates = bars.Select(b => SessionClock.DateOf(b.EpochSeconds)).ToArray();
        var high = new Dictionary<DateOnly, double>();
        var low = new Dictionary<DateOnly, double>();
        foreach (var (bar, date) in bars.Zip(dates))
        {
            high[date] = Math.Max(high.GetValueOrDefault(date, double.MinValue), bar.High);
            low[date] = Math.Min(low.GetValueOrDefault(date, double.MaxValue), bar.Low);
        }

        var order = high.Keys.Order().ToList();
        return [.. bars.Select((b, i) =>
        {
            var next = order.IndexOf(dates[i]) + 1;
            return next < order.Count ? b with { High = Math.Max(b.High, high[order[next]]), Low = Math.Min(b.Low, low[order[next]]) } : b;
        })];
    }

    [Fact]
    public void The_candidates_and_the_final_outcomes_of_a_prefix_are_those_of_the_full_run_over_all_500_daily_series_and_the_overnight_order_is_exercised()
    {
        int violations = 0, cuts = 0, candidates = 0, final = 0, pending = 0;
        var failed = new List<int>();
        foreach (var s in SeededDaily.All)
        {
            var (violated, c, cand, f, p) = Check(s, Feeds.Honest);
            violations += violated ? 1 : 0;
            if (violated)
            {
                failed.Add(s.Seed);
            }

            cuts += c;
            candidates += cand;
            final += f;
            pending += p;
        }

        output.WriteLine($"{SeededDaily.All.Count} series, {cuts} cuts: {candidates} candidates and {final} final outcomes compared, {pending} resting orders at the cut; series violating: {violations} {string.Join(",", failed.Take(10))}");
        violations.ShouldBe(0);
        cuts.ShouldBeGreaterThanOrEqualTo(800);
        final.ShouldBeGreaterThanOrEqualTo(500);
        pending.ShouldBeGreaterThanOrEqualTo(100);
    }

    [Fact]
    public void Control_the_peeking_feeds_and_a_walker_that_reads_the_fill_session_are_reported_on_at_least_ten_seeds_while_the_honest_run_reports_none()
    {
        var series = SeededDaily.All.Take(150).ToList();
        var honest = series.Count(s => Check(s, Feeds.Honest).Violated);
        var controls = new (string Name, Func<Bar[], Bar[]> Feed)[]
        {
            ("peeking feed (one minute ahead)", Feeds.Peeking),
            ("peeking extremes (the next minute's range)", Feeds.PeekingExtremes),
            ("next session's range (the fill session)", NextSessionExtremes),
        };

        output.WriteLine($"honest run on {series.Count} series: {honest} violations");
        honest.ShouldBe(0);
        foreach (var (name, feed) in controls)
        {
            var reported = series.Count(s => Check(s, feed).Violated);
            output.WriteLine($"{name}: reported on {reported} of {series.Count} seeds");
            reported.ShouldBeGreaterThanOrEqualTo(10, name);
        }
    }
}
