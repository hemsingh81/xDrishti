using XDrishti.Domain.Lab;

namespace XDrishti.Domain.Tests.Lab;

/// <summary>P5-02: the state machine, the run identity (D3) and the seeded sample of the quick test (D2, criterion 5).</summary>
public sealed class LabRunDomainTests
{
    private static readonly string Hash = new('a', 64);

    private static readonly DateOnly From = new(2025, 5, 1);

    private static readonly DateOnly To = new(2026, 4, 30);

    private static List<Guid> Ids(int count) => [.. Enumerable.Range(1, count).Select(i => new Guid(i, 0, 0, [0, 0, 0, 0, 0, 0, 0, 1]))];

    private static string Key(IReadOnlyCollection<Guid> ids, string strategy = "", string config = "c", string engine = "sim-5") =>
        LabRunIdentity.Key(strategy.Length == 0 ? Hash : strategy, "symbols", ids, From, To, "quick", config, engine);

    // state machine

    [Fact]
    public void Every_state_has_its_own_word_and_the_word_reads_back()
    {
        foreach (var state in Enum.GetValues<LabRunState>())
        {
            LabRunStates.TryParse(LabRunStates.Code(state), out var back).ShouldBeTrue();
            back.ShouldBe(state);
        }

        LabRunStates.TryParse("Queued", out _).ShouldBeFalse("the words are lower case");
        LabRunStates.TryParse(null, out _).ShouldBeFalse();
    }

    [Fact]
    public void Only_the_forward_moves_are_legal_and_a_finished_run_never_moves()
    {
        var legal = new HashSet<(LabRunState, LabRunState)>
        {
            (LabRunState.Queued, LabRunState.Running),
            (LabRunState.Queued, LabRunState.Cancelled),
            (LabRunState.Running, LabRunState.Succeeded),
            (LabRunState.Running, LabRunState.Failed),
            (LabRunState.Running, LabRunState.Cancelled),
            (LabRunState.Running, LabRunState.Interrupted),
        };

        foreach (var from in Enum.GetValues<LabRunState>())
        {
            foreach (var to in Enum.GetValues<LabRunState>())
            {
                LabRunStates.CanMove(from, to).ShouldBe(legal.Contains((from, to)), $"{from} -> {to}");
            }
        }
    }

    [Fact]
    public void The_key_is_held_by_queued_running_and_succeeded_runs_only_and_only_those_can_be_cancelled_that_have_not_finished()
    {
        LabRunState[] holding = [LabRunState.Queued, LabRunState.Running, LabRunState.Succeeded];
        foreach (var state in Enum.GetValues<LabRunState>())
        {
            LabRunStates.HoldsKey(state).ShouldBe(holding.Contains(state));
            LabRunStates.IsTerminal(state).ShouldBe(state is not (LabRunState.Queued or LabRunState.Running));
            LabRunStates.IsCancellable(state).ShouldBe(!LabRunStates.IsTerminal(state));
        }
    }

    // run identity

    [Fact]
    public void The_key_text_is_fixed_and_the_key_is_its_sha256()
    {
        var id = Guid.Parse("00000001-0000-0000-0000-000000000001");

        var text = LabRunIdentity.KeyText(Hash, "symbols", [id], From, To, "quick", "cfg", "sim-5");

        text.ShouldBe("xd-lab-run 1\nstrategy=" + Hash + "\nuniverse=symbols|00000001-0000-0000-0000-000000000001\nwindow=2025-05-01..2026-04-30\nmethod=quick\nconfig=cfg\nengine=sim-5\n");
        LabRunIdentity.Key(Hash, "symbols", [id], From, To, "quick", "cfg", "sim-5").ShouldBe(LabRunIdentity.Sha256(text));
        LabRunIdentity.Key(Hash, "symbols", [id], From, To, "quick", "cfg", "sim-5").Length.ShouldBe(64);
    }

    [Fact]
    public void The_key_ignores_the_order_of_the_instruments_and_changes_with_every_other_input()
    {
        var ids = Ids(5);
        var key = Key(ids);

        Key([.. ids.AsEnumerable().Reverse()]).ShouldBe(key);
        Key(ids, strategy: new string('b', 64)).ShouldNotBe(key, "a changed strategy version");
        Key(ids, config: "d").ShouldNotBe(key, "a changed configuration");
        Key(ids, engine: "sim-6").ShouldNotBe(key, "a changed engine");
        Key(ids[..4]).ShouldNotBe(key, "a changed universe");
        LabRunIdentity.Key(Hash, "basket", ids, From, To, "quick", "c", "sim-5").ShouldNotBe(key, "a changed label");
        LabRunIdentity.Key(Hash, "symbols", ids, From.AddDays(1), To, "quick", "c", "sim-5").ShouldNotBe(key, "a changed window");
        LabRunIdentity.Key(Hash, "symbols", ids, From, To, "full", "c", "sim-5").ShouldNotBe(key, "a changed method");
    }

    // the sample

    [Fact]
    public void A_universe_up_to_the_limit_is_used_whole_in_ascending_order()
    {
        var ids = Ids(12);

        var choice = QuickSample.Choose(ids.AsEnumerable().Reverse(), 30);

        choice.Sampled.ShouldBeFalse();
        choice.Seed.ShouldBeNull();
        choice.Ids.ShouldBe(ids);
    }

    [Fact]
    public void A_universe_of_eighty_gives_exactly_thirty_that_are_the_same_every_time_and_whatever_the_input_order()
    {
        var ids = Ids(80);

        var first = QuickSample.Choose(ids, 30);
        var again = QuickSample.Choose(ids.AsEnumerable().Reverse(), 30);

        first.Sampled.ShouldBeTrue();
        first.Seed.ShouldNotBeNull();
        first.Ids.Count.ShouldBe(30);
        first.Ids.Distinct().Count().ShouldBe(30);
        first.Ids.ShouldBe([.. first.Ids.Order()], "ascending");
        first.Ids.ShouldBeSubsetOf(ids);
        again.Ids.ShouldBe(first.Ids);
        again.Seed.ShouldBe(first.Seed);
        first.Ids.ShouldNotBe(ids.Take(30), "a shuffle, not the first thirty");
    }

    [Fact]
    public void A_different_universe_gives_a_different_seed_and_duplicates_count_once()
    {
        var ids = Ids(80);

        QuickSample.Choose(ids[..79], 30).Seed.ShouldNotBe(QuickSample.Choose(ids, 30).Seed);
        QuickSample.Choose([.. ids, .. ids], 30).Ids.ShouldBe(QuickSample.Choose(ids, 30).Ids);
        Should.Throw<ArgumentOutOfRangeException>(() => QuickSample.Choose(ids, 0));
    }

    [Fact]
    public void The_sample_is_pinned_so_that_a_release_cannot_change_it_by_accident()
    {
        var choice = QuickSample.Choose(Ids(80), 30);

        choice.Seed.ShouldBe(-8394779869572902695L);
        string.Join(",", choice.Ids.Select(i => i.ToString("D")[..8])).ShouldBe("00000001,00000004,00000009,0000000c,00000011,00000013,00000014,00000019,0000001a,0000001c,00000022,00000026,00000030,00000031,00000033,00000035,00000038,0000003c,0000003e,00000041,00000043,00000044,00000047,00000048,00000049,0000004b,0000004c,0000004d,0000004f,00000050");
    }
}
