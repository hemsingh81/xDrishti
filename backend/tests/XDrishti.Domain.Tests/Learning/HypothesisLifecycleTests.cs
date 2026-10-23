using XDrishti.Domain.Learning;

namespace XDrishti.Domain.Tests.Learning;

/// <summary>The hypothesis state machine as a table (spec P6-10): every pair of states, the facts each legal move needs, the open limit.</summary>
public sealed class HypothesisLifecycleTests
{
    private static readonly HypothesisFacts Owner = new(true, AutonomyLevel.Supervised, ExperimentOutcome.Better);

    public static TheoryData<HypothesisState, HypothesisState> AllPairs()
    {
        var data = new TheoryData<HypothesisState, HypothesisState>();
        foreach (var from in Enum.GetValues<HypothesisState>())
        {
            foreach (var to in Enum.GetValues<HypothesisState>())
            {
                data.Add(from, to);
            }
        }

        return data;
    }

    [Fact]
    public void The_table_has_exactly_the_eleven_moves()
    {
        HypothesisLifecycle.Moves.Count.ShouldBe(11);
        HypothesisLifecycle.Moves.ShouldBe(
        [
            (HypothesisState.Proposed, HypothesisState.Approved),
            (HypothesisState.Proposed, HypothesisState.Rejected),
            (HypothesisState.Proposed, HypothesisState.Expired),
            (HypothesisState.Approved, HypothesisState.Running),
            (HypothesisState.Approved, HypothesisState.Rejected),
            (HypothesisState.Approved, HypothesisState.Expired),
            (HypothesisState.Running, HypothesisState.Judged),
            (HypothesisState.Running, HypothesisState.Expired),
            (HypothesisState.Judged, HypothesisState.Accepted),
            (HypothesisState.Judged, HypothesisState.Rejected),
            (HypothesisState.Judged, HypothesisState.Expired),
        ]);
    }

    [Theory]
    [MemberData(nameof(AllPairs))]
    public void Every_pair_not_in_the_table_is_refused_with_the_legal_moves_and_every_pair_in_it_is_allowed_with_the_facts(HypothesisState from, HypothesisState to)
    {
        var refusals = HypothesisLifecycle.Refusals(from, to, Owner);

        if (HypothesisLifecycle.Moves.Contains((from, to)))
        {
            refusals.ShouldBeEmpty();
        }
        else
        {
            refusals.Count.ShouldBe(1);
            refusals[0].ShouldContain(from.Code());
        }
    }

    [Theory]
    [InlineData(HypothesisState.Accepted)]
    [InlineData(HypothesisState.Rejected)]
    [InlineData(HypothesisState.Expired)]
    public void A_final_state_cannot_move_and_says_it_is_final(HypothesisState final)
    {
        HypothesisLifecycle.Next(final).ShouldBeEmpty();
        HypothesisLifecycle.Refusals(final, HypothesisState.Proposed, Owner).Single().ShouldContain("final");
        HypothesisLifecycle.IsOpen(final).ShouldBeFalse();
    }

    [Theory]
    [InlineData(AutonomyLevel.Advisory, false, false)]
    [InlineData(AutonomyLevel.Supervised, false, false)]
    [InlineData(AutonomyLevel.Autonomous, false, true)]
    [InlineData(AutonomyLevel.Supervised, true, true)]
    [InlineData(AutonomyLevel.Advisory, true, true)]
    public void Approval_is_the_owners_unless_the_level_is_autonomous(AutonomyLevel level, bool byOwner, bool allowed)
    {
        var refusals = HypothesisLifecycle.Refusals(HypothesisState.Proposed, HypothesisState.Approved, new HypothesisFacts(byOwner, level));

        (refusals.Count == 0).ShouldBe(allowed);
    }

    [Theory]
    [InlineData(true, ExperimentOutcome.Better, 0)]
    [InlineData(true, ExperimentOutcome.NotBetter, 1)]
    [InlineData(true, ExperimentOutcome.Inconclusive, 1)]
    [InlineData(true, ExperimentOutcome.CheaperAssumption, 1)]
    [InlineData(false, ExperimentOutcome.Better, 1)]
    [InlineData(false, ExperimentOutcome.NotBetter, 2)]
    public void Accepting_needs_the_owner_and_a_Better_experiment_even_when_autonomous(bool byOwner, ExperimentOutcome outcome, int reasons)
    {
        HypothesisLifecycle.Refusals(HypothesisState.Judged, HypothesisState.Accepted, new HypothesisFacts(byOwner, AutonomyLevel.Autonomous, outcome)).Count.ShouldBe(reasons);
    }

    [Fact]
    public void Accepting_without_any_experiment_is_refused()
    {
        HypothesisLifecycle.Refusals(HypothesisState.Judged, HypothesisState.Accepted, new HypothesisFacts(true, AutonomyLevel.Supervised)).Single().ShouldContain("Better");
    }

    [Theory]
    [InlineData(HypothesisState.Approved, HypothesisState.Running)]
    [InlineData(HypothesisState.Running, HypothesisState.Judged)]
    public void Only_the_owner_starts_and_judges_at_any_autonomy_level(HypothesisState from, HypothesisState to)
    {
        foreach (var level in Enum.GetValues<AutonomyLevel>())
        {
            HypothesisLifecycle.Refusals(from, to, new HypothesisFacts(false, level)).Single().ShouldContain("owner");
            HypothesisLifecycle.Refusals(from, to, new HypothesisFacts(true, level)).ShouldBeEmpty();
        }
    }

    [Theory]
    [InlineData("proposed", true)]
    [InlineData("PROPOSED", true)]
    [InlineData("1", false)]
    [InlineData("nope", false)]
    [InlineData("", false)]
    public void A_state_is_parsed_by_its_name_only(string text, bool known)
    {
        (HypothesisStateText.TryParse(text) is not null).ShouldBe(known);
    }

    [Fact]
    public void Only_the_owner_rejects()
    {
        HypothesisLifecycle.Refusals(HypothesisState.Proposed, HypothesisState.Rejected, new HypothesisFacts(false, AutonomyLevel.Autonomous)).Single().ShouldContain("owner");
        HypothesisLifecycle.Refusals(HypothesisState.Proposed, HypothesisState.Expired, new HypothesisFacts(false, AutonomyLevel.Autonomous)).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    public void At_most_three_hypotheses_are_open(int open, bool allowed)
    {
        (HypothesisLifecycle.ProposalRefusals(new HypothesisFacts(true, AutonomyLevel.Supervised, OpenCount: open)).Count == 0).ShouldBe(allowed);
    }

    [Fact]
    public void Open_states_are_the_four_before_a_verdict()
    {
        Enum.GetValues<HypothesisState>().Where(HypothesisLifecycle.IsOpen).ShouldBe([HypothesisState.Proposed, HypothesisState.Approved, HypothesisState.Running, HypothesisState.Judged]);
    }
}
