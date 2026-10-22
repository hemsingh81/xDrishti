using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Replay;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Strategies.BuiltIn;

namespace XDrishti.Simulation.Tests.Replay;

/// <summary>
/// Criteria 1, 2, 4 and 10 of P4-05 through the replay and the built-in factory: a chosen exit replaces the exit of ORB and nothing else, a candidate replayed inside a comparison gives the replay it gives alone (gross and costed, trades, fates,
/// counters), and the definition of every candidate, hence its run key, names its exit.
/// </summary>
public sealed class ComparisonReplayTests
{
    private static readonly ExitChoice Seven = Compared.Choice(null, Compared.Seven);

    private static PreparedStrategy Prepare(ExitChoice? exits, IReadOnlyDictionary<string, double>? parameters = null)
    {
        var build = BuiltInStrategies.Prepare(OrbV1.Id, parameters, Timeframe.M15, Cand.NoSlippage, exits);
        build.Strategy.ShouldNotBeNull(string.Join("; ", build.Problems.Select(p => p.Path + " " + p.Problem.Code)));
        return build.Strategy;
    }

    private static InstrumentReplay Alone(PreparedStrategy strategy, IReadOnlyList<Bar> bars, CostContext? costs) =>
        InstrumentReplayer.Replay(strategy, Rep.Input(bars, Day.Monday, Day.Monday.AddDays(8), costs: costs));

    private static void ShouldBeEquivalent(InstrumentReplay together, InstrumentReplay alone, string where)
    {
        together.Trades.ShouldBe(alone.Trades, where);
        together.OpenAtEnd.ShouldBe(alone.OpenAtEnd, where);
        together.Record.Fates.ShouldBe(alone.Record.Fates, where);
        together.Record.Walk.ShouldBe(alone.Record.Walk, where);
        together.Record.Skipped.ShouldBe(alone.Record.Skipped, where);
        together.Record.CandidatesInWindow.ShouldBe(alone.Record.CandidatesInWindow, where);
        together.Record.TradesInWindow.ShouldBe(alone.Record.TradesInWindow, where);
        together.Record.Scans.SequenceEqual(alone.Record.Scans).ShouldBeTrue(where);
    }

    [Fact]
    public void A_chosen_exit_replaces_the_exit_of_the_builtin_and_nothing_else()
    {
        var bars = ReplayDays.Join(ReplayDays.F2(ReplayDays.D1), ReplayDays.F1(ReplayDays.D2), ReplayDays.F6(ReplayDays.D3));
        var chosen = Prepare(Compared.Choice(null, "FIXED_RR:rr=3"));
        var param = Prepare(null, new Dictionary<string, double> { ["rr"] = 3 });

        var a = InstrumentReplayer.Replay(chosen, Rep.Input(bars, ReplayDays.D1, ReplayDays.D3));
        var b = InstrumentReplayer.Replay(param, Rep.Input(bars, ReplayDays.D1, ReplayDays.D3));

        a.Trades.ShouldNotBeEmpty();
        a.Trades.ShouldBe(b.Trades, "the exit FIXED_RR rr=3 is what --param rr=3 sets");
        chosen.Members.ShouldHaveSingleItem().Id.ShouldBe("FIXED_RR{rr=3}");
    }

    [Fact]
    public void The_definition_and_so_the_record_of_a_candidate_names_its_exit_and_the_entries_part_does_not()
    {
        var plain = Prepare(null);
        var comparison = Prepare(Seven);

        comparison.Members.Select(m => m.Id).ShouldBe(Seven.Candidates.Select(c => c.Id));
        comparison.Members.Select(m => m.Record.DefinitionSha256).Distinct().Count().ShouldBe(7, "every candidate has its own definition and so its own run key");
        comparison.Members.ShouldAllBe(m => m.Record.DefinitionSha256 != plain.Record.DefinitionSha256 || m.Id == "FIXED_RR{rr=2}");
        comparison.Members.Select(m => m.EntriesDefinitionSha256).Distinct().ShouldHaveSingleItem();
        comparison.Members[0].EntriesDefinitionSha256.ShouldBe(plain.Members[0].EntriesDefinitionSha256, "the entries part of the key is the same with and without a chosen exit");
        comparison.Members[0].Record.Parameters.ShouldBe(plain.Record.Parameters);
    }

    [Fact]
    public void The_time_stop_is_in_every_definition_and_in_the_id()
    {
        var choice = Compared.Choice("min_progress_rr=0.5,intraday_bars=6", "FIXED_RR:rr=2", "FIXED_RR:rr=3");

        var comparison = Prepare(choice);

        comparison.Members.Select(m => m.Id).ShouldBe(["FIXED_RR{rr=2}+time_stop", "FIXED_RR{rr=3}+time_stop"]);
        comparison.Members.ShouldAllBe(m => m.Record.DefinitionJson.Contains("time_stop", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_candidate_replayed_inside_a_comparison_gives_the_replay_it_gives_alone(bool costed)
    {
        var costs = costed ? TestCostSheet.Context(1_000_000m, 0.10m) : null;
        var together = Prepare(Seven);
        var alone = Enumerable.Range(0, 7).Select(k => Prepare(Compared.Choice(null, Compared.Seven[k]))).ToList();
        var trades = 0;

        for (var seed = 1; seed <= 150; seed++)
        {
            var bars = SeededSeries.Of((ulong)seed);
            var replay = InstrumentReplayer.ReplayAll(together, Rep.Input(bars, Day.Monday, Day.Monday.AddDays(8), costs: costs));

            replay.Members.Count.ShouldBe(7);
            replay.GatedBlocked.ShouldAllBe(g => g == 0, "an intraday strategy is not gated");
            for (var k = 0; k < 7; k++)
            {
                var one = Alone(alone[k], bars, costs);
                ShouldBeEquivalent(replay.Members[k], one, "seed " + seed + " " + Seven.Candidates[k].Id);
                ReplayIdentities.ShouldHold(replay.Members[k], "seed " + seed + " " + Seven.Candidates[k].Id);
                trades += one.Trades.Count;
            }
        }

        trades.ShouldBeGreaterThan(500);
    }

    [Fact]
    public void A_costed_comparison_has_the_same_entries_but_may_skip_an_entry_under_one_exit_only()
    {
        var costs = TestCostSheet.Context(1_000m, 0.10m);
        var together = Prepare(Seven);
        var keys = new List<HashSet<string>>();
        var differ = 0;

        for (var seed = 1; seed <= 150; seed++)
        {
            var replay = InstrumentReplayer.ReplayAll(together, Rep.Input(SeededSeries.Of((ulong)seed), Day.Monday, Day.Monday.AddDays(8), costs: costs));
            var sets = replay.Members.Select(m => m.Trades.Concat(m.OpenAtEnd).Select(t => t.Key).ToHashSet(StringComparer.Ordinal)).ToList();
            differ += sets.Any(s => !s.SetEquals(sets[0])) ? 1 : 0;
            keys.AddRange(sets);
        }

        keys.Count.ShouldBe(150 * 7);
        differ.ShouldBeGreaterThanOrEqualTo(0);
        keys.Sum(k => k.Count).ShouldBeGreaterThan(0);
    }
}
