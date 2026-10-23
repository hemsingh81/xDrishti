using XDrishti.Domain.Learning;

namespace XDrishti.Domain.Tests.Learning;

/// <summary>The cell lifecycle of doc 09 section 7 as a table (spec P6-08): every pair of states, the gates of each legal move, the replay of rows.</summary>
public sealed class CellLifecycleTests
{
    private static readonly TransitionFacts Everything = new(true, true, true, true, 20);

    public static TheoryData<CellState, CellState> AllPairs()
    {
        var data = new TheoryData<CellState, CellState>();
        foreach (var from in Enum.GetValues<CellState>())
        {
            foreach (var to in Enum.GetValues<CellState>())
            {
                data.Add(from, to);
            }
        }

        return data;
    }

    [Fact]
    public void The_table_has_exactly_the_seven_moves_of_the_state_diagram()
    {
        CellLifecycle.Moves.ShouldBe(
        [
            (CellState.Research, CellState.Candidate),
            (CellState.Candidate, CellState.Paper),
            (CellState.Paper, CellState.Active),
            (CellState.Active, CellState.Paused),
            (CellState.Paused, CellState.Paper),
            (CellState.Active, CellState.Retired),
            (CellState.Research, CellState.Retired),
        ]);
    }

    [Theory]
    [MemberData(nameof(AllPairs))]
    public void A_move_outside_the_table_is_refused_with_a_reason_even_when_every_fact_is_stated(CellState from, CellState to)
    {
        var legal = CellLifecycle.Moves.Contains((from, to));

        var refusals = CellLifecycle.Refusals(from, to, Everything);

        CellLifecycle.IsLegal(from, to).ShouldBe(legal);
        (refusals.Count == 0).ShouldBe(legal, $"{from} to {to}");
        if (!legal)
        {
            refusals.Single().ShouldContain(from.Code());
        }
    }

    [Fact]
    public void Retired_is_final()
    {
        CellLifecycle.Next(CellState.Retired).ShouldBeEmpty();
        CellLifecycle.Refusals(CellState.Retired, CellState.Research, Everything).Single().ShouldContain("final");
    }

    [Fact]
    public void Research_needs_the_gates_to_become_a_candidate_but_not_to_be_retired()
    {
        CellLifecycle.Refusals(CellState.Research, CellState.Candidate, new TransitionFacts()).Single().ShouldContain("gates");
        CellLifecycle.Refusals(CellState.Research, CellState.Candidate, new TransitionFacts(GatesPassed: true)).ShouldBeEmpty();
        CellLifecycle.Refusals(CellState.Research, CellState.Retired, new TransitionFacts()).ShouldBeEmpty();
    }

    [Fact]
    public void Only_the_owner_moves_a_candidate_to_paper()
    {
        CellLifecycle.Refusals(CellState.Candidate, CellState.Paper, new TransitionFacts(GatesPassed: true)).Single().ShouldContain("owner");
        CellLifecycle.Refusals(CellState.Candidate, CellState.Paper, new TransitionFacts(OwnerApproved: true)).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(19, false)]
    [InlineData(20, true)]
    [InlineData(300, true)]
    public void A_cell_is_not_active_before_the_paper_days_are_over(int days, bool allowed)
    {
        var facts = new TransitionFacts(OwnerApproved: true, WithinBand: true, PaperDays: days);

        var refusals = CellLifecycle.Refusals(CellState.Paper, CellState.Active, facts);

        (refusals.Count == 0).ShouldBe(allowed);
    }

    [Fact]
    public void Active_needs_the_owner_the_band_and_the_days_and_lists_every_missing_one()
    {
        CellLifecycle.Refusals(CellState.Paper, CellState.Active, new TransitionFacts()).Count.ShouldBe(3);
        CellLifecycle.Refusals(CellState.Paper, CellState.Active, new TransitionFacts(OwnerApproved: true, WithinBand: true, PaperDays: 5), paperDaysBeforeActive: 5).ShouldBeEmpty();
    }

    [Fact]
    public void A_paused_cell_returns_to_paper_only_when_it_recovered()
    {
        CellLifecycle.Refusals(CellState.Paused, CellState.Paper, new TransitionFacts()).Single().ShouldContain("recovered");
        CellLifecycle.Refusals(CellState.Paused, CellState.Paper, new TransitionFacts(Recovered: true)).ShouldBeEmpty();
        CellLifecycle.Refusals(CellState.Active, CellState.Paused, new TransitionFacts()).ShouldBeEmpty();
    }

    [Fact]
    public void Evidence_is_needed_for_every_move_but_the_owners_approval()
    {
        CellLifecycle.Moves.Where(m => CellLifecycle.NeedsEvidence(m.From, m.To)).Count().ShouldBe(6);
        CellLifecycle.NeedsEvidence(CellState.Candidate, CellState.Paper).ShouldBeFalse();
        CellLifecycle.NeedsEvidence(CellState.Research, CellState.Active).ShouldBeFalse();
    }

    [Fact]
    public void A_revert_never_restores_active_and_every_other_state_can_be_restored()
    {
        CellLifecycle.RevertRefusals(CellState.Active).Single().ShouldContain("paper");
        Enum.GetValues<CellState>().Where(s => s != CellState.Active).ShouldAllBe(s => CellLifecycle.RevertRefusals(s).Count == 0);
    }

    [Fact]
    public void A_revert_never_un_retires_a_cell()
    {
        CellLifecycle.RevertRefusals(CellState.Research, CellState.Retired).Single().ShouldContain("final");
        CellLifecycle.RevertRefusals(CellState.Paused, CellState.Retired).Count.ShouldBe(1);
        CellLifecycle.RevertRefusals(CellState.Research, CellState.Candidate).ShouldBeEmpty();
    }

    [Fact]
    public void A_mistaken_pause_cannot_be_reverted_because_it_would_restore_active()
    {
        CellLifecycle.RevertRefusals(CellState.Active, CellState.Paused).Count.ShouldBe(1);
    }

    [Fact]
    public void A_revert_must_undo_the_row_just_before_it_and_return_to_that_rows_from()
    {
        var notPrevious = CellReplay.Of(
        [
            new CellMove(1, CellState.Research, CellState.Candidate, null),
            new CellMove(2, CellState.Candidate, CellState.Paper, null),
            new CellMove(3, CellState.Paper, CellState.Candidate, 1),
        ]);
        var wrongTarget = CellReplay.Of(
        [
            new CellMove(1, CellState.Research, CellState.Candidate, null),
            new CellMove(2, CellState.Candidate, CellState.Paper, null),
            new CellMove(3, CellState.Paper, CellState.Research, 2),
        ]);

        notPrevious.Problems.Single().ShouldContain("not the row just before");
        wrongTarget.Problems.Single().ShouldContain("not the candidate");
    }

    [Theory]
    [InlineData("research", CellState.Research)]
    [InlineData("candidate", CellState.Candidate)]
    [InlineData("paper", CellState.Paper)]
    [InlineData("active", CellState.Active)]
    [InlineData("paused", CellState.Paused)]
    [InlineData("retired", CellState.Retired)]
    public void State_codes_round_trip(string code, CellState state)
    {
        state.Code().ShouldBe(code);
        CellStateExtensions.Parse(code).ShouldBe(state);
        CellStateExtensions.Parse("Research").ShouldBeNull();
    }

    [Fact]
    public void Replaying_no_rows_gives_research()
    {
        var replay = CellReplay.Of([]);

        replay.State.ShouldBe(CellState.Research);
        replay.Problems.ShouldBeEmpty();
    }

    [Fact]
    public void Replaying_the_rows_in_order_reconstructs_the_state_with_a_revert_as_a_row()
    {
        var replay = CellReplay.Of(
        [
            new CellMove(1, CellState.Research, CellState.Candidate, null),
            new CellMove(2, CellState.Candidate, CellState.Paper, null),
            new CellMove(5, CellState.Paper, CellState.Candidate, 2),
        ]);

        replay.State.ShouldBe(CellState.Candidate);
        replay.Problems.ShouldBeEmpty();
    }

    [Fact]
    public void A_row_that_does_not_follow_the_one_before_or_reverts_an_unknown_row_is_reported()
    {
        var replay = CellReplay.Of(
        [
            new CellMove(1, CellState.Research, CellState.Candidate, null),
            new CellMove(2, CellState.Paper, CellState.Active, null),
            new CellMove(3, CellState.Active, CellState.Paper, 9),
        ]);

        replay.Problems.Count.ShouldBe(2);
        replay.Problems[0].ShouldContain("Row 2");
        replay.Problems[1].ShouldContain("Row 3");
    }

    [Fact]
    public void A_cell_key_has_six_valid_parts()
    {
        CellId.TryParse("ORB|15m|BUY|intraday|FIXED_RR|*")!.Strategy.ShouldBe("ORB");
        CellId.TryParse("ORB|15m|BUY|intraday|FIXED_RR").ShouldBeNull();
        CellId.TryParse("ORB|15m|BUY|intraday||*").ShouldBeNull();
        CellId.TryParse(null).ShouldBeNull();
    }
}
