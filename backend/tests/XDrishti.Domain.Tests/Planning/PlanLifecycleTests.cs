using XDrishti.Domain.Planning;

namespace XDrishti.Domain.Tests.Planning;

/// <summary>The plan state machine, the review cutoff, the replay and the fixed wording (spec P7-03).</summary>
public sealed class PlanLifecycleTests
{
    public static TheoryData<PlanState, PlanState> AllPairs()
    {
        var data = new TheoryData<PlanState, PlanState>();
        foreach (var from in Enum.GetValues<PlanState>())
        {
            foreach (var to in Enum.GetValues<PlanState>())
            {
                data.Add(from, to);
            }
        }

        return data;
    }

    [Fact]
    public void The_table_has_exactly_the_eleven_moves()
    {
        PlanLifecycle.Moves.Count.ShouldBe(11);
        PlanLifecycle.Next(PlanState.Draft).ShouldBe([PlanState.Published]);
        PlanLifecycle.Next(PlanState.Published).ShouldBe([PlanState.InReview, PlanState.Approved, PlanState.Rejected, PlanState.Lapsed]);
        PlanLifecycle.Next(PlanState.InReview).ShouldBe([PlanState.Approved, PlanState.Rejected, PlanState.Lapsed]);
        PlanLifecycle.Next(PlanState.Approved).ShouldBe([PlanState.Armed, PlanState.Expired]);
        PlanLifecycle.Next(PlanState.Armed).ShouldBe([PlanState.Expired]);
    }

    [Theory]
    [MemberData(nameof(AllPairs))]
    public void Every_pair_outside_the_table_is_refused_even_when_the_tickets_would_allow_it(PlanState from, PlanState to)
    {
        var allTrue = new PlanFacts(0, 1, 1, 1);
        var allZero = new PlanFacts(0, 0, 0, 0);

        var legal = PlanLifecycle.IsLegal(from, to);

        if (!legal)
        {
            PlanLifecycle.Refusals(from, to, allTrue).ShouldHaveSingleItem();
            PlanLifecycle.Refusals(from, to, allZero).ShouldHaveSingleItem();
        }
    }

    [Theory]
    [InlineData(PlanState.Rejected)]
    [InlineData(PlanState.Lapsed)]
    [InlineData(PlanState.Expired)]
    public void Final_states_have_no_moves(PlanState state)
    {
        PlanLifecycle.Next(state).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(1, 1, PlanState.Approved, false)]
    [InlineData(0, 0, PlanState.Approved, false)]
    [InlineData(0, 2, PlanState.Approved, true)]
    public void A_plan_is_approved_only_when_nothing_is_open_and_one_is_approved(int open, int approved, PlanState to, bool allowed)
    {
        PlanLifecycle.Refusals(PlanState.InReview, to, new PlanFacts(open, approved, 0, 0)).Count.ShouldBe(allowed ? 0 : 1);
    }

    [Theory]
    [InlineData(PlanState.Lapsed)]
    [InlineData(PlanState.Rejected)]
    public void A_plan_cannot_lapse_or_be_rejected_while_a_ticket_is_open_or_approved(PlanState to)
    {
        PlanLifecycle.Refusals(PlanState.InReview, to, new PlanFacts(1, 0, 0, 0)).ShouldNotBeEmpty();
        PlanLifecycle.Refusals(PlanState.InReview, to, new PlanFacts(0, 1, 0, 0)).ShouldNotBeEmpty();
        PlanLifecycle.Refusals(PlanState.InReview, to, new PlanFacts(0, 0, 0, 1)).ShouldBeEmpty();
    }

    [Fact]
    public void A_plan_is_armed_only_with_an_armed_ticket_and_in_review_only_after_a_decision()
    {
        PlanLifecycle.Refusals(PlanState.Approved, PlanState.Armed, new PlanFacts(0, 1, 0, 0)).ShouldNotBeEmpty();
        PlanLifecycle.Refusals(PlanState.Approved, PlanState.Armed, new PlanFacts(0, 0, 1, 0)).ShouldBeEmpty();
        PlanLifecycle.Refusals(PlanState.Published, PlanState.InReview, new PlanFacts(3, 0, 0, 0)).ShouldNotBeEmpty();
        PlanLifecycle.Refusals(PlanState.Published, PlanState.InReview, new PlanFacts(2, 1, 0, 0)).ShouldBeEmpty();
    }

    [Fact]
    public void Plan_facts_count_the_current_states()
    {
        PlanFacts.Of([TicketState.Proposed, TicketState.Modified, TicketState.Approved, TicketState.Armed, TicketState.Rejected, TicketState.Lapsed]).ShouldBe(new PlanFacts(2, 1, 1, 1));
    }

    [Fact]
    public void The_cutoff_lapses_every_undecided_ticket_and_ignores_rejected_candidates()
    {
        var decision = ReviewCutoff.Decide(
        [
            (TicketGroup.Primary, TicketState.Proposed),
            (TicketGroup.Reserve, TicketState.Modified),
            (TicketGroup.Primary, TicketState.Approved),
            (TicketGroup.Rejected, TicketState.Proposed),
        ]);

        decision.LapseIndexes.ShouldBe([0, 1]);
        decision.Plan.ShouldBe(PlanState.Approved);
    }

    [Fact]
    public void With_nothing_approved_the_default_is_lapse_never_approval()
    {
        var decision = ReviewCutoff.Decide([(TicketGroup.Primary, TicketState.Proposed), (TicketGroup.Primary, TicketState.Rejected)]);

        decision.LapseIndexes.ShouldBe([0]);
        decision.Plan.ShouldBe(PlanState.Lapsed);
    }

    [Fact]
    public void A_plan_the_owner_rejected_entirely_is_rejected_and_an_empty_one_lapses()
    {
        ReviewCutoff.Decide([(TicketGroup.Primary, TicketState.Rejected), (TicketGroup.Reserve, TicketState.Rejected)]).Plan.ShouldBe(PlanState.Rejected);
        ReviewCutoff.Decide([]).Plan.ShouldBe(PlanState.Lapsed);
        ReviewCutoff.Decide([(TicketGroup.Rejected, TicketState.Proposed)]).Plan.ShouldBe(PlanState.Lapsed);
    }

    [Fact]
    public void No_cutoff_outcome_is_approved_without_an_approved_ticket()
    {
        foreach (var state in Enum.GetValues<TicketState>().Where(s => s != TicketState.Approved))
        {
            ReviewCutoff.Decide([(TicketGroup.Primary, state), (TicketGroup.Reserve, state)]).Plan.ShouldNotBe(PlanState.Approved);
        }
    }

    [Fact]
    public void The_replay_gives_the_state_of_a_trail_and_reports_a_broken_one()
    {
        var good = MoveReplay.Fold(TicketState.Proposed, [(TicketState.Proposed, TicketState.Approved), (TicketState.Approved, TicketState.Armed)], TicketLifecycle.IsLegal);
        var gap = MoveReplay.Fold(TicketState.Proposed, [(TicketState.Approved, TicketState.Armed)], TicketLifecycle.IsLegal);
        var illegal = MoveReplay.Fold(TicketState.Proposed, [(TicketState.Proposed, TicketState.Armed)], TicketLifecycle.IsLegal);

        good.State.ShouldBe(TicketState.Armed);
        good.Problems.ShouldBeEmpty();
        gap.Problems.ShouldHaveSingleItem();
        illegal.Problems.ShouldHaveSingleItem().ShouldContain("not in the table");
        MoveReplay.Fold(PlanState.Draft, [], PlanLifecycle.IsLegal).State.ShouldBe(PlanState.Draft);
    }

    [Fact]
    public void The_wording_that_armed_is_not_an_order_is_pinned()
    {
        PlanWording.NoOrder.ShouldBe("No order is placed by this system. Armed means the instrument is watched and alerts are raised; a stop is a level and an alert, not a protection. Any order is entered by hand in the broker's own app.");
        PlanWording.HonestLimits.ShouldContain("no hold-out");
    }
}
