using XDrishti.Domain.Learning;

namespace XDrishti.Domain.Tests.Learning;

/// <summary>CF-02 acceptance criteria 3 to 6: the DerSimonian-Laird tau squared, the estimated Beta strength and the leave-one-out parent, with the spec's numbers.</summary>
public sealed class ShrinkageEstimatorsTests
{
    private static readonly double[] N = [10, 20, 30, 40, 25];

    private static (double, double, double)[] Siblings(double[] means) => [.. means.Select((m, i) => (m, N[i], 1.44))];

    [Fact]
    public void Tau_squared_is_exactly_zero_when_the_spread_is_smaller_than_the_noise()
    {
        var means = new[] { 0.40, 0.10, -0.05, 0.20, 0.15 };

        ShrinkageEstimators.DerSimonianLaird(Siblings(means))!.Value.ShouldBe(0.0);
        var t = ShrinkageEstimators.TauSquared(Siblings(means), 0.01);
        t.Value.ShouldBe(0.01);
        t.Source.ShouldBe("floor");
        t.Raw.ShouldBe(0.0);
        t.Siblings.ShouldBe(5);
        t.Note.ShouldContain("below the floor");
    }

    [Fact]
    public void The_leave_one_out_parent_of_the_ten_trade_cell_is_0_1065()
    {
        var means = new[] { 0.40, 0.10, -0.05, 0.20, 0.15 };

        ShrinkageEstimators.LeaveOneOutParent([.. means.Select((m, i) => (m, N[i]))], 0)!.Value.ShouldBe(0.1065, 1e-4);
    }

    [Fact]
    public void Tau_squared_is_positive_with_a_real_spread()
    {
        var means = new[] { 0.60, -0.30, 0.10, 0.45, -0.20 };

        ShrinkageEstimators.DerSimonianLaird(Siblings(means))!.Value.ShouldBe(0.0728, 1e-3);
        var t = ShrinkageEstimators.TauSquared(Siblings(means), 0.01);
        t.Source.ShouldBe("estimated");
        t.Value.ShouldBe(0.0728, 1e-3);
        t.Note.ShouldBeEmpty();
        ShrinkageEstimators.LeaveOneOutParent([.. means.Select((m, i) => (m, N[i]))], 0)!.Value.ShouldBe(10.0 / 115, 1e-9);
    }

    [Fact]
    public void Three_siblings_are_the_fewest_for_an_estimate()
    {
        var spread = new[] { (0.9, 30.0, 1.0), (-0.9, 30.0, 1.0), (0.0, 30.0, 1.0) };

        ShrinkageEstimators.TauSquared(spread, 0.01).Source.ShouldBe("estimated");
        var two = ShrinkageEstimators.TauSquared(spread[..2], 0.01);
        two.Source.ShouldBe("floor");
        two.Note.ShouldContain("fewer than 3");
        two.Raw.ShouldNotBeNull();   // the moment estimate exists for 2 but is not trusted
        var one = ShrinkageEstimators.TauSquared(spread[..1], 0.01);
        one.Source.ShouldBe("floor");
        one.Raw.ShouldBeNull();
        ShrinkageEstimators.TauSquared([], 0.01).Value.ShouldBe(0.01);
    }

    [Fact]
    public void Two_siblings_give_the_closed_form()
    {
        // w = 30 each, m_w = 0, Q = 30 (0.9^2 + 0.9^2) = 48.6, K-1 = 1, denominator 60 - 1800/60 = 30
        ShrinkageEstimators.DerSimonianLaird([(0.9, 30, 1.0), (-0.9, 30, 1.0)])!.Value.ShouldBe((48.6 - 1) / 30, 1e-9);
    }

    [Fact]
    public void An_unusable_cell_is_left_out_not_turned_into_a_number()
    {
        var cells = new[] { (0.9, 30.0, 1.0), (-0.9, 30.0, 1.0), (double.NaN, 30.0, 1.0), (0.0, 0.0, 1.0), (0.1, 30.0, 0.0) };

        ShrinkageEstimators.DerSimonianLaird(cells)!.Value.ShouldBe(ShrinkageEstimators.DerSimonianLaird(cells[..2])!.Value, 1e-12);
        ShrinkageEstimators.TauSquared(cells, 0.01).Siblings.ShouldBe(2);
        ShrinkageEstimators.DerSimonianLaird([(0.1, 30, 1.0), (0.1, 30, 1.0)])!.Value.ShouldBe(0.0);
    }

    [Fact]
    public void Scaling_R_by_two_scales_tau_squared_by_four()
    {
        var one = ShrinkageEstimators.DerSimonianLaird([(0.9, 30, 1.0), (-0.9, 50, 1.0), (0.3, 20, 1.0)])!.Value;
        var two = ShrinkageEstimators.DerSimonianLaird([(1.8, 30, 4.0), (-1.8, 50, 4.0), (0.6, 20, 4.0)])!.Value;

        two.ShouldBe(4 * one, 1e-9);
    }

    [Fact]
    public void The_leave_one_out_parent_leaves_the_cell_out()
    {
        // AC 6: A 10 trades at 0.5, B 90 at 0.0. The old parent includes the cell: 0.05 for both.
        var cells = new[] { (0.5, 10.0), (0.0, 90.0) };

        ShrinkageEstimators.LeaveOneOutParent(cells, 0)!.Value.ShouldBe(0.0);
        ShrinkageEstimators.LeaveOneOutParent(cells, 1)!.Value.ShouldBe(0.5);
        ShrinkageEstimators.LeaveOneOutParent(cells, -1)!.Value.ShouldBe(0.05, 1e-12);
        ShrinkageEstimators.LeaveOneOutParent([(0.5, 10.0)], 0).ShouldBeNull();
        ShrinkageEstimators.LeaveOneOutParent([(0.5, 10.0), (double.NaN, 5.0)], 0).ShouldBeNull();
        ShrinkageEstimators.LeaveOneOutParent([(0.5, 10.0), (double.NaN, 0.0)], 0).ShouldBeNull();   // no weight: nothing to average
    }

    [Fact]
    public void No_spread_in_the_win_rate_keeps_the_default_strength()
    {
        // AC 5: wins 6, 12, 13, 24, 13 of 10, 20, 30, 40, 25 -> Q 2.42 < 4
        var s = ShrinkageEstimators.BetaStrength([(6, 10), (12, 20), (13, 30), (24, 40), (13, 25)], 5, 200, 20);

        s.Strength.ShouldBe(20);
        s.Source.ShouldBe("default");
        s.Rho.ShouldBeNull();
        s.Note.ShouldContain("chance");
    }

    [Fact]
    public void A_genuine_spread_gives_a_strength_inside_the_range()
    {
        var s = ShrinkageEstimators.BetaStrength([(9, 10), (4, 20), (27, 30), (10, 40), (22, 25)], 5, 200, 20);

        s.Source.ShouldBe("estimated");
        s.Rho.ShouldNotBeNull();
        s.Rho!.Value.ShouldBeGreaterThan(0);
        s.Strength.ShouldBe(Math.Clamp((1 - s.Rho.Value) / s.Rho.Value, 5, 200), 1e-9);   // a wide spread is a small strength, clamped at the minimum
        s.Strength.ShouldBeInRange(5, 200);
    }

    [Fact]
    public void A_rho_of_0_0476_is_the_strength_20_of_doc_16()
    {
        ShrinkageEstimators.StrengthFromRho(0.0476, 5, 200).ShouldBe(20.0, 0.01);
        ShrinkageEstimators.StrengthFromRho(0.9, 5, 200).ShouldBe(5);
        ShrinkageEstimators.StrengthFromRho(0.001, 5, 200).ShouldBe(200);
        ShrinkageEstimators.StrengthFromRho(1.5, 5, 200).ShouldBe(5);
    }

    [Fact]
    public void Fewer_than_three_cells_or_a_pooled_rate_of_one_keep_the_default()
    {
        ShrinkageEstimators.BetaStrength([(9, 10), (1, 10)], 5, 200, 20).Note.ShouldContain("fewer than 3");
        ShrinkageEstimators.BetaStrength([(10, 10), (20, 20), (30, 30)], 5, 200, 20).Source.ShouldBe("default");
        ShrinkageEstimators.BetaStrength([(11, 10), (1, 10), (1, 10)], 5, 200, 20).Note.ShouldContain("fewer than 3");   // wins above n are not a rate
    }
}
