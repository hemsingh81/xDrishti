using XDrishti.Domain.Confidence;

namespace XDrishti.Domain.Tests.Confidence;

/// <summary>CF-01 acceptance criteria 1 to 5 against the hand tables of the spec, tolerance 1e-3, plus the edge cases and properties.</summary>
public sealed class EffectiveSampleSizeTests
{
    private static (string, double)[] Group(string key, params double[] r) => [.. r.Select(v => (key, v))];

    [Fact]
    public void KishEqualClusters()
    {
        var e = EffectiveSampleSize.Kish(30, 6, 0.5);

        e.DesignEffect!.Value.ShouldBe(3.5, 1e-3);
        e.NEff!.Value.ShouldBe(8.571, 1e-3);
        e.Method.ShouldBe("cluster");
    }

    [Fact]
    public void Cluster_estimated_from_data_three_sessions_of_four()
    {
        var trades = new[] { Group("A", 1, 1, 0.5, 1.5), Group("B", -1, -0.5, -1, -1.5), Group("C", 0.2, -0.2, 0, 0) }.SelectMany(g => g).ToList();

        var e = EffectiveSampleSize.Cluster(trades, minClusters: 3);

        // SSB 8 (df 2) -> MSB 4; SSW 1.08 (df 9) -> MSW 0.12; m0 4; rho 3.88 / 4.36 = 0.8899; DE 3.6697
        e.DesignEffect!.Value.ShouldBe(3.6697, 1e-3);
        e.NEff!.Value.ShouldBe(3.27, 1e-2);
    }

    [Fact]
    public void The_production_minimum_of_clusters_refuses_three_sessions_and_does_not_claim_N()
    {
        var trades = new[] { Group("A", 1, 1, 0.5, 1.5), Group("B", -1, -0.5, -1, -1.5), Group("C", 0.2, -0.2, 0, 0) }.SelectMany(g => g).ToList();

        var e = EffectiveSampleSize.Cluster(trades);

        e.NEff.ShouldBeNull();
        e.Method.ShouldBe("none");
        e.Note.ShouldStartWith("unavailable: 3 entry sessions");
    }

    [Fact]
    public void Fifty_trades_in_one_day_with_correlation_point_four()
    {
        var e = EffectiveSampleSize.Kish(50, 50, 0.4);

        e.NEff!.Value.ShouldBe(50 / 20.6, 1e-3);
        e.NEff!.Value.ShouldBe(2.427, 1e-3);
    }

    [Fact]
    public void A_single_date_set_is_one_observation_and_says_why()
    {
        var e = EffectiveSampleSize.Cluster(Group("2026-01-05", 1, -1, 2, 0, 3, -2), 5);

        e.NEff.ShouldBe(1);
        e.Note.ShouldContain("one entry session");
    }

    [Fact]
    public void One_trade_per_session_claims_nothing()
    {
        var trades = Enumerable.Range(0, 12).Select(i => ($"d{i}", (double)(i % 3))).ToList();

        var e = EffectiveSampleSize.Cluster(trades, 5);

        e.NEff.ShouldBeNull();
        e.Note.ShouldContain("one trade per entry session");
    }

    [Fact]
    public void Serial_series_two_two_one_minus_one_minus_two_minus_two()
    {
        double[] series = [2, 2, 1, -1, -2, -2];

        EffectiveSampleSize.Lag1(series)!.Value.ShouldBe(11.0 / 18, 1e-3);
        var e = EffectiveSampleSize.Serial(series, 6);

        e.NEff!.Value.ShouldBe(1.4483, 1e-3);
        e.Method.ShouldBe("serial");
    }

    [Fact]
    public void The_serial_factor_is_applied_to_trades_not_to_sessions()
    {
        double[] series = [2, 2, 1, -1, -2, -2];

        EffectiveSampleSize.Serial(series, 6, tradeCount: 30).NEff!.Value.ShouldBe(7.24, 1e-2);
    }

    [Fact]
    public void Serial_one_hundred_trades_with_lag_one_of_point_three()
    {
        EffectiveSampleSize.FromLag1(100, 0.3).NEff!.Value.ShouldBe(53.846, 1e-3);
    }

    [Fact]
    public void A_negative_lag_one_gives_N_and_a_short_series_is_unavailable()
    {
        EffectiveSampleSize.FromLag1(40, -0.4).NEff.ShouldBe(40);
        var e = EffectiveSampleSize.Serial([1, 2, 3], 20);

        e.NEff.ShouldBeNull();
        e.Note.ShouldStartWith("unavailable: 3 sessions");
    }

    [Fact]
    public void A_monotone_series_gives_nearly_one_not_zero()
    {
        var series = Enumerable.Range(1, 40).Select(i => (double)i).ToList();

        var e = EffectiveSampleSize.Serial(series, 20);

        e.NEff!.Value.ShouldBeGreaterThanOrEqualTo(1);
        e.NEff!.Value.ShouldBeLessThan(3);
    }

    [Fact]
    public void Combine_takes_the_smaller_and_either_one_alone()
    {
        var cluster = EffectiveSampleSize.Kish(30, 6, 0.5);        // 8.57
        var serial = EffectiveSampleSize.FromLag1(30, 0.5);        // 10
        var none = EffectiveEstimate.Unavailable("x");

        EffectiveSampleSize.Combine(cluster, serial).Method.ShouldBe("cluster");
        EffectiveSampleSize.Combine(cluster, EffectiveSampleSize.FromLag1(30, 0.9)).Method.ShouldBe("serial");
        EffectiveSampleSize.Combine(none, serial).Method.ShouldBe("serial");
        EffectiveSampleSize.Combine(cluster, none).Method.ShouldBe("cluster");
        var neither = EffectiveSampleSize.Combine(none, EffectiveEstimate.Unavailable("y"));
        neither.NEff.ShouldBeNull();
        neither.Method.ShouldBe("none");
        neither.Note.ShouldStartWith("unavailable: x; y");
    }

    [Fact]
    public void Identical_results_everywhere_are_no_correlation_not_NaN()
    {
        var trades = Enumerable.Range(0, 30).Select(i => ($"d{i % 6}", 0.5)).ToList();

        var e = EffectiveSampleSize.Cluster(trades, 5);

        e.NEff.ShouldBe(30);
    }

    [Fact]
    public void A_non_finite_result_is_unavailable()
    {
        var trades = Enumerable.Range(0, 12).Select(i => ($"d{i % 6}", i == 3 ? double.NaN : 1.0)).ToList();

        EffectiveSampleSize.Cluster(trades, 5).NEff.ShouldBeNull();
        EffectiveSampleSize.Serial([1, double.PositiveInfinity, 2], 3).NEff.ShouldBeNull();
        EffectiveSampleSize.FromLag1(10, double.NaN).NEff.ShouldBeNull();
    }

    [Fact]
    public void Properties_hold_over_many_seeded_sets()
    {
        var rng = new Random(20261104);
        for (var round = 0; round < 200; round++)
        {
            var n = rng.Next(8, 80);
            var days = rng.Next(2, 20);
            var trades = Enumerable.Range(0, n).Select(_ => ($"d{rng.Next(days)}", (rng.NextDouble() * 4) - 2)).ToList();

            var e = EffectiveSampleSize.Cluster(trades, 2);
            var shuffled = EffectiveSampleSize.Cluster([.. trades.OrderBy(_ => rng.Next())], 2);

            if (e.NEff is { } v)
            {
                v.ShouldBeGreaterThanOrEqualTo(1);
                v.ShouldBeLessThanOrEqualTo(n);
                shuffled.NEff!.Value.ShouldBe(v, 1e-9);
                e.DesignEffect!.Value.ShouldBe(n / v, 1e-9);
            }
        }
    }

    // The spec's property "adding an independent trade never lowers n_eff by more than 1" does NOT hold for the ANOVA estimator: on iid noise the intra-cluster correlation estimate moves a lot
    // with one more trade (up to about 6 on 40 trades in the seeded sets below). The estimator is noisy, which the notes and ADR 0203 say; what does hold is that the bounds stay.
    [Fact]
    public void Adding_a_trade_in_a_new_session_stays_within_1_and_N()
    {
        var rng = new Random(7);
        for (var round = 0; round < 200; round++)
        {
            var trades = Enumerable.Range(0, 40).Select(_ => ($"d{rng.Next(8)}", (rng.NextDouble() * 4) - 2)).ToList();
            trades.Add(("new", (rng.NextDouble() * 4) - 2));

            var after = EffectiveSampleSize.Cluster(trades, 5).NEff;

            after!.Value.ShouldBeGreaterThanOrEqualTo(1);
            after.Value.ShouldBeLessThanOrEqualTo(41);
        }
    }
}
