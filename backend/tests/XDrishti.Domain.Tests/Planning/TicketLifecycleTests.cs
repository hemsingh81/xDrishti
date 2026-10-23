using XDrishti.Domain.Planning;

namespace XDrishti.Domain.Tests.Planning;

/// <summary>The ticket state machine as a table (spec P7-03): every pair of the 10 x 10 states, the owner-only decisions and the lapse default.</summary>
public sealed class TicketLifecycleTests
{
    private static readonly TicketFacts Owner = new(MoveActor.Owner, TicketGroup.Primary);

    private static readonly TicketFacts System = new(MoveActor.System, TicketGroup.Primary);

    public static TheoryData<TicketState, TicketState> AllPairs()
    {
        var data = new TheoryData<TicketState, TicketState>();
        foreach (var from in Enum.GetValues<TicketState>())
        {
            foreach (var to in Enum.GetValues<TicketState>())
            {
                data.Add(from, to);
            }
        }

        return data;
    }

    [Fact]
    public void The_table_has_exactly_the_thirteen_moves()
    {
        TicketLifecycle.Moves.ShouldBe(
        [
            (TicketState.Proposed, TicketState.Approved),
            (TicketState.Proposed, TicketState.Modified),
            (TicketState.Proposed, TicketState.Rejected),
            (TicketState.Proposed, TicketState.Lapsed),
            (TicketState.Modified, TicketState.Approved),
            (TicketState.Modified, TicketState.Rejected),
            (TicketState.Modified, TicketState.Lapsed),
            (TicketState.Approved, TicketState.Armed),
            (TicketState.Approved, TicketState.Invalidated),
            (TicketState.Approved, TicketState.Expired),
            (TicketState.Armed, TicketState.Triggered),
            (TicketState.Armed, TicketState.Expired),
            (TicketState.Triggered, TicketState.Closed),
        ]);
    }

    [Theory]
    [MemberData(nameof(AllPairs))]
    public void Every_pair_outside_the_table_is_refused_naming_the_legal_moves_even_by_the_owner(TicketState from, TicketState to)
    {
        var legal = TicketLifecycle.Moves.Contains((from, to));

        var refusals = TicketLifecycle.Refusals(from, to, legal ? System with { Actor = TicketLifecycle.IsOwnerDecision(to) ? MoveActor.Owner : MoveActor.System } : Owner);

        refusals.Count.ShouldBe(legal ? 0 : 1);
        if (!legal)
        {
            refusals[0].ShouldContain(TicketLifecycle.Next(from).Count == 0 ? "final" : "can move to");
        }
    }

    [Theory]
    [InlineData(TicketState.Rejected)]
    [InlineData(TicketState.Lapsed)]
    [InlineData(TicketState.Invalidated)]
    [InlineData(TicketState.Expired)]
    [InlineData(TicketState.Closed)]
    public void Final_states_have_no_moves(TicketState state)
    {
        TicketLifecycle.Next(state).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(TicketState.Proposed, TicketState.Approved)]
    [InlineData(TicketState.Proposed, TicketState.Modified)]
    [InlineData(TicketState.Proposed, TicketState.Rejected)]
    [InlineData(TicketState.Modified, TicketState.Approved)]
    [InlineData(TicketState.Modified, TicketState.Rejected)]
    public void Approving_modifying_and_rejecting_are_refused_to_a_service(TicketState from, TicketState to)
    {
        TicketLifecycle.Refusals(from, to, System).ShouldHaveSingleItem().ShouldContain("Only the owner");
        TicketLifecycle.Refusals(from, to, Owner).ShouldBeEmpty();
    }

    [Fact]
    public void No_state_is_approved_by_anything_but_the_owner()
    {
        foreach (var from in Enum.GetValues<TicketState>())
        {
            TicketLifecycle.Refusals(from, TicketState.Approved, System).ShouldNotBeEmpty();
        }
    }

    [Theory]
    [InlineData(TicketState.Proposed, TicketState.Lapsed)]
    [InlineData(TicketState.Modified, TicketState.Lapsed)]
    [InlineData(TicketState.Approved, TicketState.Armed)]
    [InlineData(TicketState.Approved, TicketState.Invalidated)]
    [InlineData(TicketState.Approved, TicketState.Expired)]
    [InlineData(TicketState.Armed, TicketState.Triggered)]
    [InlineData(TicketState.Armed, TicketState.Expired)]
    [InlineData(TicketState.Triggered, TicketState.Closed)]
    public void The_other_moves_are_the_services_and_not_made_by_hand(TicketState from, TicketState to)
    {
        TicketLifecycle.Refusals(from, to, System).ShouldBeEmpty();
        TicketLifecycle.Refusals(from, to, Owner).ShouldHaveSingleItem().ShouldContain("services");
    }

    [Fact]
    public void A_reserve_ticket_reaches_armed_only_through_approved()
    {
        TicketLifecycle.Refusals(TicketState.Proposed, TicketState.Armed, new TicketFacts(MoveActor.System, TicketGroup.Reserve)).ShouldHaveSingleItem().ShouldContain("can move to");
        TicketLifecycle.Next(TicketState.Proposed).ShouldNotContain(TicketState.Armed);
    }

    [Theory]
    [InlineData(TicketState.Proposed, TicketState.Approved)]
    [InlineData(TicketState.Proposed, TicketState.Lapsed)]
    public void A_rejected_candidate_is_not_a_ticket_and_has_no_moves(TicketState from, TicketState to)
    {
        TicketLifecycle.Refusals(from, to, new TicketFacts(MoveActor.Owner, TicketGroup.Rejected)).ShouldHaveSingleItem().ShouldContain("not a ticket");
    }

    [Fact]
    public void The_cutoff_default_is_lapse_for_an_undecided_ticket_and_nothing_else_and_never_an_approval()
    {
        foreach (var state in Enum.GetValues<TicketState>())
        {
            var expected = state is TicketState.Proposed or TicketState.Modified ? TicketState.Lapsed : (TicketState?)null;

            TicketLifecycle.CutoffDefault(state).ShouldBe(expected);
        }

        Enum.GetValues<TicketState>().Select(TicketLifecycle.CutoffDefault).ShouldNotContain(TicketState.Approved);
        Enum.GetValues<TicketState>().Select(TicketLifecycle.CutoffDefault).ShouldNotContain(TicketState.Armed);
    }

    [Fact]
    public void The_approval_kind_has_only_the_owner()
    {
        Enum.GetValues<ApprovalKind>().ShouldBe([ApprovalKind.Owner]);
        Enum.GetValues<MoveActor>().ShouldBe([MoveActor.Owner, MoveActor.System]);
    }

    [Fact]
    public void Codes_round_trip()
    {
        foreach (var state in Enum.GetValues<TicketState>())
        {
            TicketStateText.TryParse(state.Code()).ShouldBe(state);
        }

        TicketStateText.TryParse("nope").ShouldBeNull();
        TicketGroupText.TryParse("RESERVE").ShouldBe(TicketGroup.Reserve);
    }
}
