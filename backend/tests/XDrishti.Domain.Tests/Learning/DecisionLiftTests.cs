using XDrishti.Domain.Learning;

namespace XDrishti.Domain.Tests.Learning;

/// <summary>CF-12 part 12a, AC11 (ADR 0218, spec D7): the lift of the top half of a night over the bottom half, and the share of approved tickets in the top half, with their standard errors.</summary>
public sealed class DecisionLiftTests
{
    private static readonly DateOnly Day = new(2026, 4, 6);

    [Fact]
    public void The_lift_by_hand_is_0_30_with_a_standard_error_of_0_3795_and_a_z_of_0_79_not_significant()
    {
        var (lift, se) = DecisionLift.LiftFrom(20, 0.25, 20, -0.05, 1.2);

        lift.ShouldBe(0.30, 1e-12);
        se.ShouldBe(0.3795, 1e-4);
        (lift / se).ShouldBe(0.79, 0.005);
    }

    [Fact]
    public void Twenty_one_of_thirty_approved_tickets_in_the_top_half_is_a_z_of_2_19()
    {
        DecisionLift.ShareZ(21, 30).ShouldBe(2.19, 0.005);
        DecisionLift.ShareZ(15, 30).ShouldBe(0, 1e-12);
    }

    [Fact]
    public void Candidates_are_ranked_within_their_own_night_and_the_halves_are_compared()
    {
        // four nights of four candidates; the top two of each night win +1, the bottom two lose -1 with a little spread
        var candidates = new List<LiftCandidate>();
        for (var d = 0; d < 4; d++)
        {
            var day = Day.AddDays(d);
            candidates.Add(new LiftCandidate($"{d}a", day, 0.9, 1.1, Approved: true));
            candidates.Add(new LiftCandidate($"{d}b", day, 0.7, 0.9, Approved: false));
            candidates.Add(new LiftCandidate($"{d}c", day, 0.4, -0.9, Approved: true));
            candidates.Add(new LiftCandidate($"{d}d", day, 0.2, -1.1, Approved: false));
        }

        var result = DecisionLift.Compute(candidates);

        (result.TopTrades, result.BottomTrades).ShouldBe((8, 8));
        result.TopMeanR!.Value.ShouldBe(1.0, 1e-12);
        result.BottomMeanR!.Value.ShouldBe(-1.0, 1e-12);
        result.Lift!.Value.ShouldBe(2.0, 1e-12);
        result.LiftZ!.Value.ShouldBeGreaterThan(1.96);
        (result.ApprovedTickets, result.ApprovedInTop).ShouldBe((8, 4));
        result.ApprovedTopShare.ShouldBe(0.5);
        result.ApprovedZ!.Value.ShouldBe(0, 1e-12);
        result.Text.ShouldContain("significant");
        result.Text.ShouldContain("not significant"); // the approvals are at chance
    }

    [Fact]
    public void The_middle_candidate_of_an_odd_night_and_unscored_candidates_are_in_neither_half()
    {
        var candidates = new[]
        {
            new LiftCandidate("a", Day, 0.9, 1, true), new LiftCandidate("b", Day, 0.5, 0, true), new LiftCandidate("c", Day, 0.1, -1, true), new LiftCandidate("x", Day, null, 5, true),
            new LiftCandidate("single", Day.AddDays(1), 0.7, 2, true),
        };

        var result = DecisionLift.Compute(candidates);

        (result.TopTrades, result.BottomTrades).ShouldBe((1, 1));
        (result.ApprovedTickets, result.ApprovedInTop).ShouldBe((2, 1));
        result.Lift.ShouldBeNull(); // one trade a half is too few for a standard error
    }

    [Fact]
    public void Ties_in_the_score_are_broken_by_the_key_so_the_input_order_does_not_matter()
    {
        var candidates = new[]
        {
            new LiftCandidate("a", Day, 0.5, 1, false), new LiftCandidate("b", Day, 0.5, 2, false), new LiftCandidate("c", Day, 0.5, 3, false), new LiftCandidate("d", Day, 0.5, 4, false),
        };

        DecisionLift.Compute([.. candidates.Reverse()]).ShouldBe(DecisionLift.Compute(candidates));
    }

    [Fact]
    public void An_ungraded_candidate_counts_as_a_ticket_but_not_in_the_mean()
    {
        var candidates = new[] { new LiftCandidate("a", Day, 0.9, null, true), new LiftCandidate("b", Day, 0.1, -1, false) };

        var result = DecisionLift.Compute(candidates);

        result.TopTrades.ShouldBe(0);
        result.TopMeanR.ShouldBeNull();
        result.ApprovedInTop.ShouldBe(1);
    }

    [Fact]
    public void Nothing_to_read_says_so()
    {
        var result = DecisionLift.Compute([]);

        result.Lift.ShouldBeNull();
        result.ApprovedTopShare.ShouldBeNull();
        result.Text.ShouldContain("no approved ticket");
    }
}
