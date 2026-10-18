using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Running;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Strategies.BuiltIn;
using XDrishti.Strategies.Model;

namespace XDrishti.Simulation.Tests.Running;

/// <summary>
/// Criterion 14 of P3-03a through <c>Simulator.Run</c> (the scanner's own half is <c>LoopLookAheadPropertyTests</c>): for 100 seeded series and a cut <c>k</c> (a random one, the decision bar of a candidate, the bar before it, and the
/// exit minute of a trade and the minute before it), the candidates with <c>DecisionBarIndex ≤ k</c>, trace included, and every outcome settled at an index at most <c>k</c> are equal to those of the full run when the bars after
/// <c>k</c> are removed and when they are replaced by arbitrary values. Both sides of ORB v1 and the strategies of F19, F20 and F21 (invalidate, setup, per-session features) are run. The <b>negative controls</b> are doctored
/// inputs built from the public API only, each reported on at least 10 seeds while the honest run reports none on the very same inputs: the peeking feed of the scanner's property and the peeking walker input of P3-03 (every
/// low and high widened by the next minute's). Counters up to <c>k</c> are observable on the direct-scanner path only; a <c>Run</c> exposes the final ones.
/// </summary>
public sealed class SimulatorLookAheadPropertyTests
{
    private static readonly ulong[] Seeds = [.. Enumerable.Range(1, 100).Select(i => (ulong)i)];

    private static readonly (string Name, StrategyDefinition Definition)[] Strategies =
    [
        ("ORB", OrbV1.Create()),
        ("F19 invalidate", TestStrategies.InvalidateOnLowUnderRange()),
        ("F20 setup", TestStrategies.SetupAfterTen()),
        ("F21 per-session", TestStrategies.CrossVwap()),
    ];

    private sealed record Counts(int Candidates, int Outcomes, int Trades);

    private static string Key(CandidateOutcome o) => o.Candidate.Side + "@" + o.Candidate.DecisionBarIndex;

    /// <summary>The cuts of a seed: a random one, a candidate's own bar and the bar before it, and a stop or target exit and the minute before it (where a feed that looks ahead would have acted earlier).</summary>
    private static List<int> Cuts(ulong seed, Bar[] bars, SimulationResult full)
    {
        var rng = new SeededSeries(seed * 7919);
        var cuts = new List<int> { 1 + rng.Int(bars.Length - 2) };
        if (full.Outcomes.Count > 0)
        {
            var at = full.Outcomes[rng.Int(full.Outcomes.Count)].Candidate.DecisionBarIndex;
            cuts.Add(at);
            cuts.Add(at - 1);
        }

        var exits = full.Outcomes.Where(o => o.Walk.Trade is { ExitReason: ExitReason.Stop or ExitReason.Target }).ToList();
        if (exits.Count > 0)
        {
            var chosen = exits[rng.Int(exits.Count)];
            var exit = LookAhead.SettledAt(chosen.Candidate, bars, chosen.Walk);
            cuts.Add(exit);
            cuts.Add(exit - 1);
        }

        return [.. cuts.Where(k => k >= 1 && k < bars.Length - 1).Distinct()];
    }

    /// <summary>Whether the property is violated on this seed for any cut, with the feed transformed by <paramref name="feed"/>, and how much was compared.</summary>
    private static (bool Violated, Counts Compared) Check(Simulator simulator, ulong seed, Func<Bar[], Bar[]> feed)
    {
        var bars = SeededSeries.Of(seed);
        var fullFeed = feed(bars);
        var full = Sim.Run(simulator, fullFeed);
        var violated = false;
        var candidates = 0;
        var outcomes = 0;
        var trades = 0;
        foreach (var k in Cuts(seed, bars, full))
        {
            var expectedCandidates = full.Outcomes.Where(o => o.Candidate.DecisionBarIndex <= k).Select(o => Render.Candidate(o.Candidate)).ToList();
            var settled = full.Outcomes.Where(o => LookAhead.SettledAt(o.Candidate, fullFeed, o.Walk) <= k).ToList();
            candidates += expectedCandidates.Count;
            outcomes += settled.Count;
            trades += settled.Count(o => o.Walk.Trade is not null);

            foreach (var cut in new[] { Sim.Run(simulator, feed([.. bars.Take(k + 1)])), Sim.Run(simulator, feed(Feeds.Replaced(bars, k, seed))) })
            {
                violated |= !cut.Outcomes.Where(o => o.Candidate.DecisionBarIndex <= k).Select(o => Render.Candidate(o.Candidate)).SequenceEqual(expectedCandidates);
                foreach (var outcome in settled)
                {
                    var same = cut.Outcomes.FirstOrDefault(o => Key(o) == Key(outcome));
                    violated |= same is null || OutcomeFormatter.Render(XDrishti.Domain.Common.Result.Success(same.Walk)) != OutcomeFormatter.Render(XDrishti.Domain.Common.Result.Success(outcome.Walk));
                }
            }
        }

        return (violated, new Counts(candidates, outcomes, trades));
    }

    [Fact]
    public void The_generator_makes_enough_candidates_on_both_sides_and_enough_settled_outcomes_for_the_property_to_mean_something()
    {
        var orb = Sim.Create(OrbV1.Create());

        var results = Seeds.Select(seed => Sim.Run(orb, SeededSeries.Of(seed))).ToList();
        var compared = Seeds.Select(seed => Check(orb, seed, Feeds.Honest).Compared).ToList();

        var candidates = results.SelectMany(r => r.Outcomes).ToList();
        TestContext.Current.TestOutputHelper?.WriteLine($"candidates {candidates.Count}; compared candidates {compared.Sum(c => c.Candidates)}, settled outcomes {compared.Sum(c => c.Outcomes)}, trades {compared.Sum(c => c.Trades)}");
        candidates.Count.ShouldBeGreaterThanOrEqualTo(60);
        candidates.Count(o => o.Candidate.Side == Side.Buy).ShouldBeGreaterThanOrEqualTo(20);
        candidates.Count(o => o.Candidate.Side == Side.Sell).ShouldBeGreaterThanOrEqualTo(20);
        compared.Sum(c => c.Candidates).ShouldBeGreaterThanOrEqualTo(60);
        compared.Sum(c => c.Trades).ShouldBeGreaterThanOrEqualTo(30);
    }

    [Fact]
    public void The_honest_run_reports_no_violation_on_any_seed_for_any_strategy()
    {
        var violations = new List<string>();
        foreach (var (name, definition) in Strategies)
        {
            var simulator = Sim.Create(definition);
            violations.AddRange(Seeds.Where(seed => Check(simulator, seed, Feeds.Honest).Violated).Select(seed => name + " seed " + seed));
        }

        violations.ShouldBeEmpty();
    }

    [Fact]
    public void The_peeking_feed_is_reported_on_at_least_ten_seeds_while_the_honest_run_reports_none_on_the_same_inputs()
    {
        var orb = Sim.Create(OrbV1.Create());

        var reported = Seeds.Count(seed => Check(orb, seed, Feeds.Peeking).Violated);
        var honest = Seeds.Count(seed => Check(orb, seed, Feeds.Honest).Violated);

        TestContext.Current.TestOutputHelper?.WriteLine($"the peeking feed is caught on {reported} of {Seeds.Length} seeds");
        honest.ShouldBe(0);
        reported.ShouldBeGreaterThanOrEqualTo(10);
    }

    [Fact]
    public void The_peeking_walker_input_of_the_walkers_property_is_reported_on_at_least_ten_seeds_while_the_honest_run_reports_none()
    {
        var orb = Sim.Create(OrbV1.Create());

        var reported = Seeds.Count(seed => Check(orb, seed, Feeds.PeekingExtremes).Violated);
        var honest = Seeds.Count(seed => Check(orb, seed, Feeds.Honest).Violated);

        TestContext.Current.TestOutputHelper?.WriteLine($"the peeking input is caught on {reported} of {Seeds.Length} seeds");
        honest.ShouldBe(0);
        reported.ShouldBeGreaterThanOrEqualTo(10);
    }
}
