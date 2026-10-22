using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Running;
using XDrishti.Simulation.Scanning;
using XDrishti.Simulation.Tests.Support;

namespace XDrishti.Simulation.Tests.Running;

/// <summary>
/// Criterion 4 of P4-05 (D12 a, b, e) as properties over 5,000 seeded series (ORB v1 on the invented sessions of <see cref="SeededSeries"/>, both sides, the modelled-free fills of the other tests of P4): a candidate walked inside a comparison of the
/// seven policies gives the outcomes of the same candidate alone, in every field, whatever the order of the candidates, and a candidate reads only its own column of the union tape. The <b>negative control</b> is a comparison doctored so that a
/// candidate is walked with the exit state of its neighbour (a shared exit between candidates): the same comparison reports it on at least 10 seeds while the honest run reports none on the very same inputs.
/// </summary>
public sealed class ComparisonPropertyTests(ITestOutputHelper output)
{
    private const int Series = 5_000;

    private static readonly ExitChoice Seven = Compared.Choice(null, Compared.Seven);

    private static readonly SimulatorComparison Together = Compared.Comparison(Seven);

    private static readonly Simulator[] Alone = [.. Enumerable.Range(0, Seven.Candidates.Count).Select(k => Compared.Alone(Seven, k))];

    private static string Show(SimulationResult result) =>
        string.Join("\n", result.Outcomes.Select(o => o.Candidate.Side + "@" + o.Candidate.DecisionBarIndex + " " + (o.Walk.Trade is { } t ? string.Join(",", t.ExitReason, t.ExitEpochSeconds, t.ExitPrice, t.RExact, t.MaeR, t.MfeR, t.Legs?.Count ?? 0) : o.Walk.NoTrade?.ToString())));

    private static bool Same(SimulationResult a, SimulationResult b) =>
        a.Outcomes.Count == b.Outcomes.Count
        && a.Outcomes.Zip(b.Outcomes).All(p => p.First.Walk == p.Second.Walk && p.First.Candidate.DecisionBarIndex == p.Second.Candidate.DecisionBarIndex && p.First.Candidate.Side == p.Second.Candidate.Side)
        && a.Walk == b.Walk;

    [Fact]
    public void A_candidate_walked_inside_a_comparison_gives_the_outcomes_it_gives_alone_over_5000_series()
    {
        var bad = new System.Collections.Concurrent.ConcurrentBag<string>();
        var trades = new int[7];
        var legs = 0;

        Parallel.For(1, Series + 1, seed =>
        {
            var bars = SeededSeries.Of((ulong)seed);
            var together = Together.Run(Cand.Test, bars, Sim.Tick);
            for (var k = 0; k < 7; k++)
            {
                var alone = Alone[k].Run(Cand.Test, bars, Sim.Tick);
                if (!Same(together.Value.Members[k], alone.Value))
                {
                    bad.Add(seed + " " + Seven.Candidates[k].Id);
                }

                Interlocked.Add(ref trades[k], alone.Value.Trades.Count + alone.Value.OpenAtEnd.Count);
                Interlocked.Add(ref legs, alone.Value.Trades.Count(t => t.Legs is not null));
            }
        });

        output.WriteLine("trades by policy: " + string.Join(", ", trades) + "; with legs: " + legs);
        bad.Take(10).ShouldBeEmpty();
        trades.ShouldAllBe(n => n >= 1_000, "the property is not vacuous for any policy");
        legs.ShouldBeGreaterThan(1_000);
    }

    [Fact]
    public void Permuting_the_candidates_permutes_only_the_order_of_the_results_over_500_series()
    {
        var reversed = Compared.Choice(null, [.. Compared.Seven.Reverse()]);
        var other = Compared.Comparison(reversed);
        var bad = new System.Collections.Concurrent.ConcurrentBag<int>();

        Parallel.For(1, 501, seed =>
        {
            var bars = SeededSeries.Of((ulong)seed);
            var a = Together.Run(Cand.Test, bars, Sim.Tick).Value;
            var b = other.Run(Cand.Test, bars, Sim.Tick).Value;
            for (var k = 0; k < 7; k++)
            {
                if (!Same(a.Members[k], b.Members[6 - k]))
                {
                    bad.Add(seed);
                }
            }
        });

        bad.ShouldBeEmpty();
    }

    /// <summary>The comparison with a shared exit: candidate k is walked with the side run (the exit plan, the policy and the rule) of candidate k + 1.</summary>
    private static List<SimulationResult> Leaky(IReadOnlyList<Bar> bars)
    {
        var results = new List<SimulationResult>();
        var members = Together.Members;
        var perMember = members.Select(_ => new List<CandidateOutcome>()).ToArray();
        for (var s = 0; s < 2; s++)
        {
            var parts = Seven.Candidates.Select(c => Scan.Compile(Compared.Orb(Seven, [c]), s == 0 ? Side.Buy : Side.Sell)).ToList();
            var side = parts[0].WithExitsOf(parts.Skip(1));
            var scanner = EntryScanner.Create(side, Cand.Test).Value;
            foreach (var bar in bars)
            {
                scanner.Step(bar);
            }

            for (var k = 0; k < members.Count; k++)
            {
                var walked = members[k].WalkSide(members[(k + 1) % members.Count].SideRuns[s], scanner, bars, Sim.Tick, null, null, gated: false);
                perMember[k].AddRange(walked.Value);
            }
        }

        for (var k = 0; k < members.Count; k++)
        {
            var ordered = perMember[k].OrderBy(o => o.Candidate.DecisionEpochSeconds).ThenBy(o => o.Candidate.Side).ToList();
            results.Add(new SimulationResult(members[k].HeaderOf(), ordered, [], WalkCounters.Empty));
        }

        return results;
    }

    [Fact]
    public void A_comparison_that_shares_an_exit_between_candidates_fails_the_property_on_at_least_10_series_and_the_honest_one_on_none()
    {
        var caught = 0;
        var honest = 0;

        for (var seed = 1; seed <= 200; seed++)
        {
            var bars = SeededSeries.Of((ulong)seed);
            var leaky = Leaky(bars);
            var together = Together.Run(Cand.Test, bars, Sim.Tick).Value;
            var differs = false;
            var honestDiffers = false;
            for (var k = 0; k < 7; k++)
            {
                var alone = Alone[k].Run(Cand.Test, bars, Sim.Tick).Value;
                differs |= Show(leaky[k]) != Show(alone);
                honestDiffers |= !Same(together.Members[k], alone);
            }

            caught += differs ? 1 : 0;
            honest += honestDiffers ? 1 : 0;
        }

        output.WriteLine("caught " + caught + " of 200, honest reports " + honest);
        caught.ShouldBeGreaterThanOrEqualTo(10);
        honest.ShouldBe(0);
    }
}
