using XDrishti.Domain.Research;
using XDrishti.Simulation.Running;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Simulation.Walking;

namespace XDrishti.Simulation.Tests.Running;

/// <summary>
/// Criteria 3, 4 and 10 of P4-05 at the simulator: one scan and N walks. Every candidate of a comparison gives the outcomes it gives alone (no leakage between candidates), the candidates walk the same entries, the scan counters are the
/// one scan's, the exit tape holds the union of the features and nothing else, and a candidate that cannot be run is refused by name with its index.
/// </summary>
public sealed class SimulatorComparisonTests
{
    private static readonly ExitChoice Seven = Compared.Choice(null, Compared.Seven);

    public static TheoryData<string> Days => [.. Cases.FDays.Select(c => c.Name)];

    private static List<(string Key, WalkOutcome Walk)> Outcomes(SimulationResult result) =>
        [.. result.Outcomes.Select(o => (o.Candidate.Side + "@" + o.Candidate.DecisionBarIndex, o.Walk))];

    [Theory]
    [MemberData(nameof(Days))]
    public void Every_candidate_gives_together_the_outcomes_it_gives_alone(string day)
    {
        var bars = Cases.F(day).Bars;
        var together = Compared.Comparison(Seven).Run(Cand.Test, bars, Sim.Tick);
        together.IsSuccess.ShouldBeTrue();

        for (var k = 0; k < Seven.Candidates.Count; k++)
        {
            var alone = Sim.Run(Compared.Alone(Seven, k), bars, Cand.Test);

            Outcomes(together.Value.Members[k]).ShouldBe(Outcomes(alone), Seven.Candidates[k].Id + " " + day);
            together.Value.Members[k].Walk.ShouldBe(alone.Walk, Seven.Candidates[k].Id + " " + day);
            together.Value.Members[k].Trades.ShouldBe(alone.Trades, Seven.Candidates[k].Id + " " + day);
        }
    }

    [Theory]
    [MemberData(nameof(Days))]
    public void The_candidates_walk_the_same_entries_and_differ_only_in_what_happens_after_them(string day)
    {
        var together = Compared.Comparison(Seven).Run(Cand.Test, Cases.F(day).Bars, Sim.Tick).Value;

        var reference = together.Members[0].Outcomes;
        foreach (var member in together.Members.Skip(1))
        {
            member.Outcomes.Count.ShouldBe(reference.Count);
            for (var i = 0; i < reference.Count; i++)
            {
                (member.Outcomes[i].Candidate with { Trace = DecisionTrace.Empty }).ShouldBe(reference[i].Candidate with { Trace = DecisionTrace.Empty });
                if (member.Outcomes[i].Walk.Trade is { } trade && reference[i].Walk.Trade is { } first)
                {
                    (trade.Key, trade.EntryEpochSeconds, trade.EntryPrice, trade.EntryFilledPrice, trade.StopPrice, trade.Side, trade.TriggerBar, trade.DecisionEpochSeconds)
                        .ShouldBe((first.Key, first.EntryEpochSeconds, first.EntryPrice, first.EntryFilledPrice, first.StopPrice, first.Side, first.TriggerBar, first.DecisionEpochSeconds));
                }
                else
                {
                    member.Outcomes[i].Walk.NoTrade.ShouldBe(reference[i].Walk.NoTrade);
                }
            }
        }
    }

    [Fact]
    public void The_scan_runs_once_and_every_member_shares_its_counters()
    {
        var bars = Cases.F("F1").Bars;

        var together = Compared.Comparison(Seven).Run(Cand.Test, bars, Sim.Tick).Value;

        together.Members.Count.ShouldBe(7);
        together.Members.ShouldAllBe(m => ReferenceEquals(m.Scans, together.Scans));
        together.Scans.Count.ShouldBe(2);
        together.Scans.ShouldAllBe(s => s.Bars == bars.Count, "each side's frame stepped each minute once, not seven times");
        Sim.Run(Compared.Alone(Seven, 0), bars, Cand.Test).Scans.ShouldBe(together.Scans);
    }

    [Fact]
    public void The_union_tape_holds_the_features_some_candidate_reads_and_each_candidate_reads_its_own_column()
    {
        var parts = Seven.Candidates.Select(c => Scan.Compile(Compared.Orb(Seven, [c]), Side.Buy)).ToList();
        var side = parts[0].WithExitsOf(parts.Skip(1));

        var scanner = Scan.Run(side, Fixtures.OrbDays.Single(f => f.Name == "F1").Bars);

        scanner.Tape.Features.Count.ShouldBe(4, "atr(3) is read by two candidates and held once, then atr(5), the lowest of 3 and the ema of 5");
        scanner.Tape.Features.Distinct().Count().ShouldBe(scanner.Tape.Features.Count);
        var alone = Scan.Run(Scan.Compile(Compared.Orb(Seven, [Seven.Candidates[4]]), Side.Buy), Fixtures.OrbDays.Single(f => f.Name == "F1").Bars);
        alone.Tape.Features.Count.ShouldBe(1, "a candidate alone records only what it reads");
        scanner.Tape.Features.ShouldContain(alone.Tape.Features[0]);
    }

    [Fact]
    public void A_candidate_the_simulator_cannot_run_is_refused_by_name_with_its_index()
    {
        var choice = Compared.Choice(null, "FIXED_RR:rr=2", "FIXED_RR:rr=20");

        var build = SimulatorComparison.Create([.. choice.Candidates.Select(c => Compared.Orb(choice, [c]))], XDrishti.Domain.Market.Bars.Timeframe.M15);

        build.Comparison.ShouldBeNull();
        build.Problems.ShouldContain(p => p.Path.StartsWith("candidates[1].", StringComparison.Ordinal));
        build.Problems.ShouldNotContain(p => p.Path.StartsWith("candidates[0].", StringComparison.Ordinal));
    }

    [Fact]
    public void A_comparison_needs_two_candidates()
    {
        var choice = Compared.Choice(null, "FIXED_RR:rr=2");

        Should.Throw<ArgumentException>(() => SimulatorComparison.Create([Compared.Orb(choice, choice.Candidates)], XDrishti.Domain.Market.Bars.Timeframe.M15));
    }

    [Fact]
    public void Permuting_the_candidates_permutes_only_the_order_of_the_results()
    {
        var forward = Compared.Choice(null, Compared.Seven);
        var backward = Compared.Choice(null, [.. Compared.Seven.Reverse()]);
        var bars = SeededSeries.Of(11);

        var a = Compared.Comparison(forward).Run(Cand.Test, bars, Sim.Tick).Value;
        var b = Compared.Comparison(backward).Run(Cand.Test, bars, Sim.Tick).Value;

        for (var k = 0; k < 7; k++)
        {
            Outcomes(b.Members[6 - k]).ShouldBe(Outcomes(a.Members[k]), forward.Candidates[k].Id);
        }
    }

    [Fact]
    public void A_list_of_values_of_one_policy_and_two_candidates_of_one_policy_are_compared_like_any_others()
    {
        var choice = Compared.Choice(null, "FIXED_RR:rr=1.5|2|2.5", "PARTIAL_BE:book_pct=50,t1_rr=1,t2_rr=2|3", "PARTIAL_BE:book_pct=30,t1_rr=0.5,t2_rr=2", "ATR_TRAIL:mult=2|3,activate_at_rr=0.5,atr_period=5");
        var together = Compared.Comparison(choice);
        var trades = 0;

        for (var seed = 1; seed <= 200; seed++)
        {
            var bars = SeededSeries.Of((ulong)seed);
            var result = together.Run(Cand.Test, bars, Sim.Tick).Value;
            for (var k = 0; k < choice.Candidates.Count; k++)
            {
                var alone = Compared.Alone(choice, k).Run(Cand.Test, bars, Sim.Tick).Value;
                Outcomes(result.Members[k]).ShouldBe(Outcomes(alone), choice.Candidates[k].Id + " seed " + seed);
                trades += alone.Trades.Count;
            }
        }

        choice.Candidates.Count.ShouldBe(8);
        trades.ShouldBeGreaterThan(500);
    }

    [Fact]
    public void The_trade_key_does_not_depend_on_the_exit()
    {
        var together = Compared.Comparison(Seven).Run(Cand.Test, Cases.F("F1").Bars, Sim.Tick).Value;

        var keys = together.Members.Select(m => m.Trades.Concat(m.OpenAtEnd).Select(t => t.Key).Order(StringComparer.Ordinal).ToList()).ToList();

        keys[0].ShouldNotBeEmpty();
        keys.ShouldAllBe(k => k.SequenceEqual(keys[0]));
    }
}
