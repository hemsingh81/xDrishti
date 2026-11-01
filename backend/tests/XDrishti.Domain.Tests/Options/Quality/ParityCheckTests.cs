using XDrishti.Domain.Options.Quality;
using static XDrishti.Domain.Tests.Options.Quality.ChainBuilder;

namespace XDrishti.Domain.Tests.Options.Quality;

public sealed class ParityCheckTests
{
    private static readonly OptionQualityThresholds T = new();

    private static readonly int[] Strikes = [9_600, 9_800, 10_000, 10_200, 10_400, 10_600];

    // Hand fixture of the spec (x100, paise): K0 = 100.00, Cmid 5.00 -> here 10.00 and Pmid 9.00, so C - P = 1.00 and F = 101.00 = 10_100 paise (DF = 1, the expiry is the session day).
    // Every other strike has C = 1000 and P = C - (F - K) = K - 9_100, so each residual is zero. A bid and ask 5 paise either side of the mid give tol = (10 + 10) / 2 + 5 tick = 15.
    private static List<ChainPoint> Consistent(int brokenPutAt = 0, int brokenBy = 90)
    {
        var points = new List<ChainPoint>();
        foreach (var k in Strikes)
        {
            points.Add(Quote(k, true, 1_000));
            points.Add(Quote(k, false, k - 9_100 + (k == brokenPutAt ? brokenBy : 0)));
        }

        return points;
    }

    [Fact]
    public void A_consistent_chain_passes_with_every_tested_strike_inside_the_tolerance()
    {
        var check = ParityCheck.Evaluate(Consistent(), Session, T);

        check.Status.ShouldBe(QualityStatus.Pass);
        check.Value.ShouldBe(1.0);
        check.N.ShouldBe(5); // six strikes, the nearest the money is not tested
    }

    [Fact]
    public void The_hand_fixture_put_mid_6_00_instead_of_5_10_is_beyond_the_tolerance()
    {
        // K = 102.00: C - P = -1.00 = DF (F - K) passes; a put 0.90 too high leaves a residual of 90 paise against a tolerance of 15.
        var good = ParityCheck.Evaluate(Consistent(), Session, T);
        var bad = ParityCheck.Evaluate(Consistent(10_200, 90), Session, T);

        good.Value.ShouldBe(1.0);
        bad.Value.ShouldBe(4.0 / 5);
        bad.Status.ShouldBe(QualityStatus.Warn);
    }

    [Fact]
    public void A_residual_of_exactly_the_tolerance_passes_and_one_paise_more_does_not()
    {
        ParityCheck.Evaluate(Consistent(10_200, 15), Session, T).Value.ShouldBe(1.0);
        ParityCheck.Evaluate(Consistent(10_200, 16), Session, T).Value.ShouldBe(4.0 / 5);
    }

    [Fact]
    public void Two_broken_strikes_of_five_fail()
    {
        var points = Consistent(10_200).Where(p => !(p.StrikePx == 10_400 && !p.IsCall)).Append(Quote(10_400, false, 10_400 - 9_100 + 90)).ToList();

        var check = ParityCheck.Evaluate(points, Session, T);

        check.Value.ShouldBe(3.0 / 5);
        check.Status.ShouldBe(QualityStatus.Fail);
    }

    [Fact]
    public void Fewer_than_five_strikes_with_both_sides_quoted_is_unavailable_too_few_points()
    {
        var points = Consistent().Where(p => p.StrikePx <= 10_200).ToList();

        var check = ParityCheck.Evaluate(points, Session, T);

        check.Status.ShouldBe(QualityStatus.Unavailable);
        check.Reason.ShouldBe("too_few_points");
    }

    [Fact]
    public void A_chain_with_no_bid_or_ask_is_unavailable_never_pass()
    {
        var points = Strikes.SelectMany(k => new[] { Quoted(k, true, null, null), Quoted(k, false, null, null) }).ToList();

        var check = ParityCheck.Evaluate(points, Session, T);

        check.Status.ShouldBe(QualityStatus.Unavailable);
        check.Reason.ShouldBe("no_two_sided_quote");
    }

    [Fact]
    public void A_zero_bid_is_no_quote_and_a_crossed_book_is_left_out_and_counted()
    {
        var points = Strikes.SelectMany(k => new[] { Quoted(k, true, 0, 100), Quoted(k, false, 120, 100, 16) }).ToList();

        var check = ParityCheck.Evaluate(points, Session, T);

        check.Status.ShouldBe(QualityStatus.Unavailable);
        check.Reason.ShouldBe("no_two_sided_quote");
        check.Detail.ShouldNotBeNull().ShouldContain("\"crossed_quotes\":6");
    }

    [Fact]
    public void A_crossed_strike_is_not_tested_while_the_others_still_are()
    {
        var points = Consistent().Where(p => p.StrikePx != 10_600).Concat([Quoted(10_600, true, 1_200, 1_000, 16), Quoted(10_600, false, 1_400, 1_500)]).ToList();

        var check = ParityCheck.Evaluate(points, Session, T);

        check.N.ShouldBe(4); // five usable strikes, the nearest the money not tested
        check.Status.ShouldBe(QualityStatus.Pass);
    }

    [Fact]
    public void The_forward_discounts_at_the_set_rate_over_the_days_to_expiry()
    {
        // 365 days to expiry at 6.5 %: DF = exp(-0.065). The chain is built for that DF, so it passes with the rate and breaks with a rate of zero.
        var df = Math.Exp(-0.065);
        var expiry = Session.AddDays(365);
        var points = new List<ChainPoint>();
        const int Forward = 10_100;
        foreach (var k in Strikes)
        {
            var call = 5_000;
            var put = (int)Math.Round(call - (df * (Forward - k)));
            points.Add(Quote(k, true, call, 5, null, expiry));
            points.Add(Quote(k, false, put, 5, null, expiry));
        }

        var rate = ParityCheck.Evaluate(points, Session, T);
        var zero = ParityCheck.Evaluate(points, Session, T with { Rate = 0 });

        rate.Value.ShouldBe(1.0);
        zero.Value.ShouldNotBeNull().ShouldBeLessThan(1.0);
    }
}
