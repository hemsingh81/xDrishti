using XDrishti.Domain.Options.Quality;
using static XDrishti.Domain.Tests.Options.Quality.ChainBuilder;

namespace XDrishti.Domain.Tests.Options.Quality;

public sealed class GreeksCheckTests
{
    private static readonly OptionQualityThresholds T = new();

    private static readonly int[] Strikes = [9_600, 9_800, 10_000, 10_200, 10_400];

    private static readonly double[] CallDeltas = [0.9, 0.7, 0.5, 0.3, 0.1];

    private static readonly DateOnly Near = new(2026, 10, 15);

    private static readonly DateOnly Far = new(2026, 11, 26);

    /// <summary>Five strikes, both sides, one expiry: 5 iv + 5 delta + 5 gamma + 5 vega per side and 4 delta-order pairs per side, 48 points.</summary>
    private static List<ChainPoint> Clean(DateOnly? expiry = null, DateTimeOffset? ts = null, double iv = 20)
    {
        var points = new List<ChainPoint>();
        for (var i = 0; i < Strikes.Length; i++)
        {
            points.Add(Greek(Strikes[i], true, iv, CallDeltas[i], expiry: expiry, ts: ts));
            points.Add(Greek(Strikes[i], false, iv, CallDeltas[i] - 1, expiry: expiry, ts: ts));
        }

        return points;
    }

    [Fact]
    public void A_clean_chain_passes()
    {
        var check = GreeksCheck.Evaluate(Clean(), Session, T);

        check.Status.ShouldBe(QualityStatus.Pass);
        check.Value.ShouldBe(0);
        check.N.ShouldBe(48);
    }

    [Fact]
    public void A_delta_that_rises_with_the_strike_beyond_the_tolerance_is_a_violation()
    {
        var points = Clean();
        points[6] = Greek(10_200, true, 20, 0.64); // 0.5 at 10000 then 0.64 at 10200: a rise of 0.14

        var check = GreeksCheck.Evaluate(points, Session, T);

        check.Detail.ShouldNotBeNull().ShouldContain("\"delta_order_violations\":1");
        check.Status.ShouldBe(QualityStatus.Warn); // 1 of 48 points = 2.08 %, above the 2 % warn line and below the 10 % fail line
    }

    [Fact]
    public void A_rise_inside_the_tolerance_is_not_a_violation()
    {
        var points = Clean();
        points[6] = Greek(10_200, true, 20, 0.51); // 0.5 at 10000 then 0.51 at 10200: a rise of 0.01, inside 0.02

        GreeksCheck.Evaluate(points, Session, T).Status.ShouldBe(QualityStatus.Pass);
    }

    [Fact]
    public void Many_violations_fail()
    {
        var points = Clean().Select(p => p with { Delta = 0.3 }).Concat([Greek(10_600, true, 0, -0.5), Greek(10_800, true, 400, 1.5)]).ToList();

        GreeksCheck.Evaluate(points, Session, T).Status.ShouldBe(QualityStatus.Fail);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(300.0)]
    [InlineData(-5.0)]
    public void An_iv_of_zero_or_at_or_above_the_ceiling_or_negative_is_a_violation(double iv)
    {
        var points = Clean();
        points[0] = Greek(9_600, true, iv, 0.9);

        GreeksCheck.Evaluate(points, Session, T).Detail.ShouldNotBeNull().ShouldContain("\"range_violations\":1");
    }

    [Fact]
    public void A_negative_gamma_or_vega_and_a_delta_out_of_range_are_violations()
    {
        var points = Clean();
        points[0] = Greek(9_600, true, 20, 0.9, gamma: -0.1, vega: -1);
        points[1] = Greek(9_600, false, 20, 0.2); // put delta must be in [-1, 0]

        GreeksCheck.Evaluate(points, Session, T).Detail.ShouldNotBeNull().ShouldContain("\"range_violations\":3");
    }

    [Fact]
    public void A_row_flagged_bad_greeks_is_one_point_and_one_violation()
    {
        var points = Clean().Append(Greek(10_600, true, null, null, null, null, flags: 8)).ToList();

        var check = GreeksCheck.Evaluate(points, Session, T);

        check.Detail.ShouldNotBeNull().ShouldContain("\"range_violations\":1");
        check.N.ShouldBe(49);
    }

    [Fact]
    public void Null_values_are_not_points_and_a_zero_is_not_a_null()
    {
        var nulls = Strikes.Select(k => Greek(k, true, null, null, null, null)).ToList();
        var zeros = Strikes.Select(k => Greek(k, true, 0, 0, 0, 0)).ToList();

        var unknown = GreeksCheck.Evaluate(nulls, Session, T);
        var zero = GreeksCheck.Evaluate(zeros, Session, T);

        unknown.Status.ShouldBe(QualityStatus.Unavailable);
        unknown.Reason.ShouldBe("too_few_points");
        zero.N.ShouldBeGreaterThan(0);
        zero.Detail.ShouldNotBeNull().ShouldContain("\"range_violations\":5"); // five ivs of zero; delta 0, gamma 0 and vega 0 are in range
    }

    [Fact]
    public void Fewer_than_five_points_is_unavailable()
    {
        var check = GreeksCheck.Evaluate([Greek(10_000, true, 20, null, null, null), Greek(10_000, false, 20, null, null, null)], Session, T);

        check.Status.ShouldBe(QualityStatus.Unavailable);
        check.Reason.ShouldBe("too_few_points");
    }

    [Fact]
    public void A_far_expiry_with_a_lower_total_variance_than_the_near_one_is_a_calendar_violation()
    {
        // near: 20 % for 3 days; far: 3 % for 45 days -> iv^2 T falls from 3.3 to 1.1.
        var points = Clean(Near, iv: 20).Concat(Clean(Far, iv: 3)).ToList();

        GreeksCheck.Evaluate(points, Session, T).Detail.ShouldNotBeNull().ShouldContain("\"calendar_violations\":1");
    }

    [Fact]
    public void A_far_expiry_with_a_higher_total_variance_is_fine_and_a_small_dip_is_inside_the_tolerance()
    {
        var rising = Clean(Near, iv: 20).Concat(Clean(Far, iv: 20)).ToList();
        var dipping = Clean(Near, iv: 20).Concat(Clean(Far, iv: 20 * Math.Sqrt(3.0 / 45) * 0.99)).ToList(); // variance at 99 % of the near one

        GreeksCheck.Evaluate(rising, Session, T).Detail.ShouldNotBeNull().ShouldContain("\"calendar_violations\":0");
        GreeksCheck.Evaluate(dipping, Session, T).Detail.ShouldNotBeNull().ShouldContain("\"calendar_violations\":0");
    }
}
