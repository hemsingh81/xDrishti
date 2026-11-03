using XDrishti.Domain.Options.Pricing;

namespace XDrishti.Domain.Tests.Options.Pricing;

/// <summary>Spec P14-05 criterion 6: every reason of the closed list is reached by a hand case; nothing throws on a number; the result cannot carry both a value and a reason.</summary>
public sealed class PricingHonestStatesTests
{
    [Theory]
    [InlineData(0.0, 100.0, 0.25, 1.0)]
    [InlineData(-5.0, 100.0, 0.25, 1.0)]
    [InlineData(100.0, double.NaN, 0.25, 1.0)]
    [InlineData(double.PositiveInfinity, 100.0, 0.25, 1.0)]
    [InlineData(100.0, 100.0, double.NaN, 1.0)]
    [InlineData(100.0, 100.0, double.PositiveInfinity, 1.0)]
    public void A_non_positive_or_non_finite_forward_strike_or_time_is_bad_input(double f, double k, double t, double df)
    {
        Black76.Price(OptionKind.Call, f, k, t, df, 0.2).Reason.ShouldBe(UnavailableReason.BadInput);
        Black76.Greeks(OptionKind.Put, f, k, t, df, 0.2).Reason.ShouldBe(UnavailableReason.BadInput);
        ImpliedVol.Solve(OptionKind.Call, 5.0, f, k, t, df).Reason.ShouldBe(UnavailableReason.BadInput);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-0.2)]
    [InlineData(5.000001)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void A_volatility_outside_zero_to_five_is_bad_vol(double sigma)
    {
        Black76.Price(OptionKind.Call, 100, 100, 0.25, 1.0, sigma).Reason.ShouldBe(UnavailableReason.BadVol);
        Black76.Greeks(OptionKind.Call, 100, 100, 0.25, 1.0, sigma).Reason.ShouldBe(UnavailableReason.BadVol);
    }

    [Fact]
    public void A_volatility_of_exactly_five_is_priced()
    {
        Black76.Price(OptionKind.Call, 100, 100, 0.25, 1.0, 5.0).IsAvailable.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    public void Time_at_or_below_zero_is_expired(double t)
    {
        Black76.Price(OptionKind.Call, 100, 100, t, 1.0, 0.2).Reason.ShouldBe(UnavailableReason.Expired);
        BlackScholes.Price(OptionKind.Call, 100, 100, t, 0.0, 0.2).Reason.ShouldBe(UnavailableReason.Expired);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-0.5)]
    [InlineData(1.0000001)]
    [InlineData(double.NaN)]
    public void A_discount_factor_outside_zero_to_one_is_bad_rate(double df)
    {
        Black76.Price(OptionKind.Call, 100, 100, 0.25, df, 0.2).Reason.ShouldBe(UnavailableReason.BadRate);
        ParityForward.From([new StrikeQuotes(100, 4.9, 5.1, 3.9, 4.1)], 100.0, df).Reason.ShouldBe(UnavailableReason.BadRate);
    }

    [Fact]
    public void A_negative_rate_in_the_spot_form_is_bad_rate()
    {
        BlackScholes.Price(OptionKind.Call, 100, 100, 0.25, -0.01, 0.2).Reason.ShouldBe(UnavailableReason.BadRate);
        BlackScholes.Greeks(OptionKind.Call, 100, 100, 0.25, double.NaN, 0.2).Reason.ShouldBe(UnavailableReason.BadRate);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void A_price_that_is_not_a_positive_number_is_bad_price(double price)
    {
        ImpliedVol.Solve(OptionKind.Call, price, 100, 100, 0.25, 1.0).Reason.ShouldBe(UnavailableReason.BadPrice);
    }

    [Fact]
    public void A_put_at_or_below_its_intrinsic_is_below_intrinsic_and_a_put_at_the_strike_is_above_the_upper_bound()
    {
        ImpliedVol.Solve(OptionKind.Put, 9.0, 90, 100, 0.25, 1.0).Reason.ShouldBe(UnavailableReason.BelowIntrinsic);
        ImpliedVol.Solve(OptionKind.Put, 100.0, 100, 100, 0.25, 1.0).Reason.ShouldBe(UnavailableReason.AboveUpperBound);
        ImpliedVol.Solve(OptionKind.Put, 95.0, 100, 100, 0.25, 0.9).Reason.ShouldBe(UnavailableReason.AboveUpperBound);
    }

    [Fact]
    public void A_price_between_intrinsic_and_the_bracket_has_no_root_in_it()
    {
        // ATM call: price at sigma 1e-4 is about 0.002, so 0.001 lies below the bracket; 90 lies above the price at sigma 5 (about 78.9).
        ImpliedVol.Solve(OptionKind.Call, 0.001, 100, 100, 0.25, 1.0).Reason.ShouldBe(UnavailableReason.NoConvergence);
        ImpliedVol.Solve(OptionKind.Call, 90.0, 100, 100, 0.25, 1.0).Reason.ShouldBe(UnavailableReason.NoConvergence);
    }

    [Fact]
    public void A_chain_with_no_strike_quoted_on_both_sides_has_no_forward()
    {
        var oneSided = new[] { new StrikeQuotes(100, 4.9, 5.1, null, null), new StrikeQuotes(105, 0.0, 5.1, 3.9, 4.1), new StrikeQuotes(110, 5.2, 5.1, 3.9, 4.1) };

        ParityForward.From(oneSided, 100.0, 1.0).Reason.ShouldBe(UnavailableReason.NoTwoSidedQuote);
        ParityForward.From([], 100.0, 1.0).Reason.ShouldBe(UnavailableReason.NoTwoSidedQuote);
        ParityForward.From([new StrikeQuotes(100, 4.9, 5.1, 3.9, 4.1)], double.NaN, 1.0).Reason.ShouldBe(UnavailableReason.BadInput);
    }

    [Fact]
    public void Only_a_null_argument_throws()
    {
        Should.Throw<ArgumentNullException>(() => ParityForward.From(null!, 100.0, 1.0));
    }

    [Fact]
    public void A_result_carries_a_value_or_a_reason_never_both()
    {
        var ok = Priced.Of(1.5);
        var bad = Priced.Unavailable<double>(UnavailableReason.BadPrice);

        ok.IsAvailable.ShouldBeTrue();
        ok.Reason.ShouldBeNull();
        ok.TryGet(out var v).ShouldBeTrue();
        v.ShouldBe(1.5);
        bad.IsAvailable.ShouldBeFalse();
        bad.Reason.ShouldBe(UnavailableReason.BadPrice);
        bad.TryGet(out var zero).ShouldBeFalse();
        zero.ShouldBe(0.0);
        Should.Throw<InvalidOperationException>(() => bad.Value);
        default(PricedValue<double>).IsAvailable.ShouldBeFalse();
        default(PricedValue<double>).Reason.ShouldBe(UnavailableReason.BadInput);
        (ok == Priced.Of(1.5)).ShouldBeTrue();
        (ok != bad).ShouldBeTrue();
    }

    [Fact]
    public void Every_reason_has_a_distinct_snake_case_label()
    {
        var labels = Enum.GetValues<UnavailableReason>().Select(r => r.Label()).ToList();

        labels.Count.ShouldBe(10);
        labels.Distinct().Count().ShouldBe(10);
        labels.ShouldAllBe(l => l.All(ch => char.IsAsciiLetterLower(ch) || ch == (char)95) && !l.Contains(' ', StringComparison.Ordinal));
        UnavailableReason.VolNotIdentified.Label().ShouldBe("vol_not_identified");
    }

    [Fact]
    public void Extreme_and_non_finite_inputs_never_throw_and_never_return_a_non_finite_number()
    {
        double[] values = [double.NaN, double.PositiveInfinity, double.NegativeInfinity, 0.0, -0.0, double.Epsilon, double.MinValue, double.MaxValue, 1e-300, 1e300, 1e-12, 1e12, -1.0, 1.0, 0.2, 100.0];
        var finite = 0;
        foreach (var f in values)
        {
            foreach (var k in values)
            {
                foreach (var t in values)
                {
                    foreach (var s in new[] { double.NaN, 0.0, 1e-4, 0.2, 5.0, double.MaxValue })
                    {
                        foreach (var kind in Enum.GetValues<OptionKind>())
                        {
                            CheckFinite(Black76.Price(kind, f, k, t, 0.99, s), ref finite);
                            CheckFinite(Black76.Price(kind, f, k, t, 1.0, s), ref finite);
                            CheckGreeks(Black76.Greeks(kind, f, k, t, 0.99, s));
                            CheckFinite(BlackScholes.Price(kind, f, k, t, 0.065, s), ref finite);
                            CheckFinite(ImpliedVol.Solve(kind, s, f, k, t, 0.99), ref finite);
                            CheckFinite(ImpliedVol.Solve(kind, 1.0, f, k, t, 1.0), ref finite);
                        }
                    }
                }
            }
        }

        finite.ShouldBeGreaterThan(0);
    }

    private static void CheckFinite(PricedValue<double> result, ref int finite)
    {
        if (result.IsAvailable)
        {
            double.IsFinite(result.Value).ShouldBeTrue();
            result.Reason.ShouldBeNull();
            finite++;
        }
        else
        {
            result.Reason.ShouldNotBeNull();
        }
    }

    private static void CheckGreeks(PricedValue<OptionGreeks> result)
    {
        if (result.IsAvailable)
        {
            var g = result.Value;
            double.IsFinite(g.Delta).ShouldBeTrue();
            double.IsFinite(g.Gamma).ShouldBeTrue();
            double.IsFinite(g.Vega).ShouldBeTrue();
            double.IsFinite(g.Theta).ShouldBeTrue();
        }
    }
}
