using XDrishti.Domain.Shadow;

namespace XDrishti.Domain.Tests.Shadow;

/// <summary>CF-08 criteria 8 and 9 by hand: the effective n of the edge count and the pace in sessions.</summary>
public sealed class EdgeCountTests
{
    private static EdgeDay[] Days(int sessions, int candidates, int graded) => [.. Enumerable.Repeat(new EdgeDay(candidates, graded), sessions)];

    [Fact]
    public void The_design_effect_of_five_candidates_at_rho_03_is_22_and_of_six_is_25()
    {
        EdgeCount.DesignEffect(5, 0.3).ShouldBe(2.2, 1e-12);
        EdgeCount.DesignEffect(6, 0.3).ShouldBe(2.5, 1e-12);
        EdgeCount.DesignEffect(1, 0.3).ShouldBe(1.0);
    }

    [Fact]
    public void Raw_240_with_six_candidates_a_session_at_rho_03_is_96_effective()
    {
        var count = EdgeCount.Compute(Days(40, 6, 6));

        count.RawN.ShouldBe(240);
        count.EffectiveN.ShouldBe(96.0, 1e-9);
        count.Rho.ShouldBe(0.3);
        count.RhoIsPlaceholder.ShouldBeTrue();
        count.Sessions.ShouldBe(40);
    }

    [Fact]
    public void Raw_100_with_five_candidates_a_session_is_4545_effective()
    {
        var count = EdgeCount.Compute(Days(20, 5, 5));

        count.RawN.ShouldBe(100);
        count.EffectiveN.ShouldBe(100 / 2.2, 1e-9);
        count.EffectiveN.ShouldBe(45.45, 0.005);
    }

    [Fact]
    public void One_candidate_a_day_gives_effective_n_equal_to_raw_n()
    {
        var count = EdgeCount.Compute(Days(30, 1, 1));

        count.EffectiveN.ShouldBe(30.0, 1e-12);
        count.RawN.ShouldBe(30);
    }

    [Fact]
    public void A_session_with_no_candidate_contributes_nothing_and_is_not_a_session()
    {
        var count = EdgeCount.Compute([new EdgeDay(0, 0), new EdgeDay(6, 6)]);

        count.RawN.ShouldBe(6);
        count.EffectiveN.ShouldBe(2.4, 1e-12);
        count.Sessions.ShouldBe(1);
    }

    [Fact]
    public void A_stated_correlation_is_not_the_placeholder_and_one_out_of_range_is_refused()
    {
        var count = EdgeCount.Compute(Days(10, 5, 5), 0.5);

        count.RhoIsPlaceholder.ShouldBeFalse();
        count.Rho.ShouldBe(0.5);
        count.EffectiveN.ShouldBe(50 / 3.0, 1e-9);
        Should.Throw<ArgumentOutOfRangeException>(() => EdgeCount.Compute(Days(1, 5, 5), 1.5));
        Should.Throw<ArgumentOutOfRangeException>(() => EdgeCount.Compute(Days(1, 5, 5), -0.1));
    }

    [Fact]
    public void Sixty_effective_trades_take_25_sessions_at_24_a_session_and_sixty_armed_trades_40_at_15()
    {
        EdgeCount.SessionsToReach(60, 6 / 2.5).ShouldBe(25);
        EdgeCount.SessionsToReach(60, 1.5).ShouldBe(40);
        EdgeCount.SessionsToReach(60, 0).ShouldBeNull();
    }

    [Fact]
    public void The_eta_from_the_observed_rate_needs_ten_graded_trades_and_is_a_straight_line()
    {
        EdgeCount.Compute(Days(1, 6, 6)).EtaSessions(60).ShouldBeNull(); // 6 trades only
        var count = EdgeCount.Compute(Days(10, 6, 6)); // 60 raw, 24 effective, 2.4 a session

        count.EtaSessions(60).ShouldBe(15); // (60 - 24) / 2.4
        EdgeCount.Compute(Days(40, 6, 6)).EtaSessions(60).ShouldBe(0); // 96 effective: reached
    }

    [Fact]
    public void The_line_names_raw_n_effective_n_rho_and_the_placeholder()
    {
        var line = EdgeCount.Compute(Days(40, 6, 6)).Line();

        line.ShouldContain("raw n 240");
        line.ShouldContain("effective n 96.0");
        line.ShouldContain("rho 0.30");
        line.ShouldContain("placeholder");
    }
}
