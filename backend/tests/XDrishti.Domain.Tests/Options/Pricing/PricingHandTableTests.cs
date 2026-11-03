using XDrishti.Domain.Options.Pricing;

namespace XDrishti.Domain.Tests.Options.Pricing;

/// <summary>Spec P14-05 criteria 1 and 8: the hand table, the IV cases, the forward case, the units. The table values were recomputed independently (Python erfc) before use; all agree to 6 decimals.</summary>
public sealed class PricingHandTableTests
{
    private const double Tol = 1e-6;

    public static TheoryData<double, double, double, double, double, double, double, double, double, double, double, double, double, double, double> Rows => new()
    {
        { 100, 100, 0.20, 0.25, 0.0, 100.000000, 1.000000, 3.987761, 3.987761, 0.519939, -0.480061, 0.039844, 0.199222, -0.021833, -0.021833 },
        { 100, 105, 0.25, 0.5, 0.065, 103.303389, 0.968022, 6.309906, 7.952263, 0.498499, -0.501501, 0.022567, 0.282093, -0.027075, -0.008974 },
        { 22000, 22000, 0.15, 7.0 / 365.0, 0.065, 22027.441758, 0.998754, 196.231665, 168.824094, 0.528061, -0.471939, 0.000871, 12.124371, -15.024294, -11.111367 },
    };

    [Theory]
    [MemberData(nameof(Rows))]
    public void The_hand_table_rows_match_in_price_greeks_forward_and_discount(double s, double k, double sigma, double t, double r, double f, double df, double call, double put, double dCall, double dPut, double gamma, double vega, double thCall, double thPut)
    {
        var forward = s * Math.Exp(r * t);
        forward.ShouldBe(f, Tol);
        Math.Exp(-r * t).ShouldBe(df, Tol);
        BlackScholes.Price(OptionKind.Call, s, k, t, r, sigma).Value.ShouldBe(call, Tol);
        BlackScholes.Price(OptionKind.Put, s, k, t, r, sigma).Value.ShouldBe(put, Tol);
        var gc = BlackScholes.Greeks(OptionKind.Call, s, k, t, r, sigma).Value;
        var gp = BlackScholes.Greeks(OptionKind.Put, s, k, t, r, sigma).Value;
        gc.Delta.ShouldBe(dCall, Tol);
        gp.Delta.ShouldBe(dPut, Tol);
        gc.Gamma.ShouldBe(gamma, Tol);
        gp.Gamma.ShouldBe(gamma, Tol);
        gc.Vega.ShouldBe(vega, Tol);
        gp.Vega.ShouldBe(vega, Tol);
        gc.Theta.ShouldBe(thCall, Tol);
        gp.Theta.ShouldBe(thPut, Tol);
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void The_same_numbers_come_from_black_76_on_the_forward(double s, double k, double sigma, double t, double r, double f, double df, double call, double put, double dCall, double dPut, double gamma, double vega, double thCall, double thPut)
    {
        _ = (s, f, df, dCall, dPut, gamma, vega, thCall, thPut);
        var forward = s * Math.Exp(r * t);
        var discount = Math.Exp(-r * t);
        Black76.Price(OptionKind.Call, forward, k, t, discount, sigma).Value.ShouldBe(call, Tol);
        Black76.Price(OptionKind.Put, forward, k, t, discount, sigma).Value.ShouldBe(put, Tol);
        Black76.Greeks(OptionKind.Call, forward, k, t, discount, sigma).Value.Theta.ShouldBe(thCall, Tol);
    }

    [Fact]
    public void Row_A_is_the_phase_criterion_call_equals_100_times_2_N_005_minus_1()
    {
        var expected = 100.0 * ((2.0 * 0.5199388058383725) - 1.0);

        Black76.Price(OptionKind.Call, 100, 100, 0.25, 1.0, 0.20).Value.ShouldBe(expected, 1e-9);
    }

    [Fact]
    public void Row_B_obeys_parity_c_minus_p_equals_df_times_f_minus_k()
    {
        var c = BlackScholes.Price(OptionKind.Call, 100, 105, 0.5, 0.065, 0.25).Value;
        var p = BlackScholes.Price(OptionKind.Put, 100, 105, 0.5, 0.065, 0.25).Value;

        (c - p).ShouldBe(-1.642357, Tol);
    }

    [Fact]
    public void Vega_is_per_volatility_point_and_theta_per_calendar_day()
    {
        var g = Black76.Greeks(OptionKind.Call, 100, 100, 0.25, 1.0, 0.20).Value;

        g.Vega.ShouldBe(0.199222, Tol);
        Math.Abs(g.Vega - 19.922).ShouldBeGreaterThan(1.0);
        g.Theta.ShouldBe(-0.021833, Tol);
        Math.Abs(g.Theta - (-7.969)).ShouldBeGreaterThan(1.0);
        var bumped = Black76.Price(OptionKind.Call, 100, 100, 0.25, 1.0, 0.21).Value - Black76.Price(OptionKind.Call, 100, 100, 0.25, 1.0, 0.20).Value;
        bumped.ShouldBe(g.Vega, 1e-3);
    }

    [Theory]
    [InlineData(OptionKind.Call, 100.0, 100.0, 100.0, 0.25, 0.0, 3.987761, 0.20)]
    [InlineData(OptionKind.Put, 100.0, 105.0, 103.303389, 0.5, 0.065, 7.952263, 0.25)]
    [InlineData(OptionKind.Call, 22000.0, 22000.0, 22027.441758, 7.0 / 365.0, 0.065, 196.231665, 0.15)]
    public void The_iv_of_a_table_price_comes_back(OptionKind kind, double s, double k, double f, double t, double r, double price, double expected)
    {
        var discount = Math.Exp(-r * t);

        // Plan error recorded (ADR 0191): the table prices have 6 decimals, so a sigma within 1e-8 is only possible from the unrounded price; from the printed price the sigma is good to the rounding (5e-7) divided by the raw vega.
        var exact = Black76.Price(kind, s * Math.Exp(r * t), k, t, discount, expected).Value;
        ImpliedVol.Solve(kind, exact, s * Math.Exp(r * t), k, t, discount).Value.ShouldBe(expected, 1e-8);
        var tolerance = 1e-6 / Black76.RawVega(s * Math.Exp(r * t), k, t, discount, expected);
        ImpliedVol.Solve(kind, price, f, k, t, discount).Value.ShouldBe(expected, tolerance);
    }

    [Theory]
    [InlineData(OptionKind.Put, 47123.92666737991, 58655.35977751695, 0.19521407879711752, 0.9841892681418839, 0.10122204701998276)]
    [InlineData(OptionKind.Call, 21337.111583487807, 16998.773956775436, 0.5894744496614202, 0.9409147423446234, 0.060577523471073036)]
    [InlineData(OptionKind.Call, 300.5941534808826, 255.69945040263858, 0.4037251698942334, 0.996824420529578, 0.05229703568902902)]
    public void Regression_deep_in_the_money_round_trips_that_missed_1e_8_with_a_single_polish_step(OptionKind kind, double f, double k, double t, double df, double sigma)
    {
        var price = Black76.Price(kind, f, k, t, df, sigma).Value;

        ImpliedVol.Solve(kind, price, f, k, t, df).Value.ShouldBe(sigma, 1e-8);
    }

    [Fact]
    public void The_stated_iv_refusals_return_their_reasons()
    {
        ImpliedVol.Solve(OptionKind.Call, 9.50, 100, 90, 0.25, 1.0).Reason.ShouldBe(UnavailableReason.BelowIntrinsic);
        ImpliedVol.Solve(OptionKind.Call, 100.0, 100, 100, 0.25, 1.0).Reason.ShouldBe(UnavailableReason.AboveUpperBound);
    }

    [Fact]
    public void A_far_out_of_the_money_price_pins_no_volatility()
    {
        // F 100, K 150, T 0.25, DF 1: at price 1e-9 the root is near sigma 0.115 where the raw vega is about 1e-7, below the limit 1e-8 x F = 1e-6.
        var result = ImpliedVol.Solve(OptionKind.Call, 1e-9, 100, 150, 0.25, 1.0);

        result.Reason.ShouldBe(UnavailableReason.VolNotIdentified);
    }

    [Fact]
    public void The_parity_forward_of_the_p14_04_fixture_is_101()
    {
        var quotes = new[] { new StrikeQuotes(100, 4.9, 5.1, 3.9, 4.1) };

        ParityForward.From(quotes, 100.0, 1.0).Value.Value.ShouldBe(101.0, 1e-12);
    }

    [Fact]
    public void The_forward_takes_the_strike_nearest_the_underlying_and_the_lower_one_on_a_tie_and_skips_crossed_or_one_sided_strikes()
    {
        var quotes = new[]
        {
            new StrikeQuotes(90, 10.9, 11.1, 0.9, 1.1),
            new StrikeQuotes(100, 7.0, 6.0, 3.0, 3.2),
            new StrikeQuotes(110, 4.9, 5.1, null, 6.0),
            new StrikeQuotes(120, 4.9, 5.1, 3.9, 4.1),
            new StrikeQuotes(130, 4.9, 5.1, 3.9, 4.1),
        };

        // 100 is crossed and 110 is one-sided; 90 is 15 from 105 and 120 is 15 away: the tie takes 90, F = 90 + (11 - 1).
        ParityForward.From(quotes, 105.0, 1.0).Value.Value.ShouldBe(100.0, 1e-12);
    }

    [Fact]
    public void The_forward_divides_the_mid_difference_by_the_discount_factor()
    {
        var quotes = new[] { new StrikeQuotes(100, 4.9, 5.1, 3.9, 4.1) };

        ParityForward.From(quotes, 100.0, 0.5).Value.Value.ShouldBe(102.0, 1e-12);
    }

    [Fact]
    public void The_rate_forward_is_s_times_e_to_rt_and_carries_its_label()
    {
        var rateForward = ParityForward.FromRate(100, 0.065, 0.5).Value;
        rateForward.Value.ShouldBe(100 * Math.Exp(0.0325), 1e-12);
        rateForward.Source.ShouldBe(ForwardSource.ForwardFromRate);
        ParityForward.FromRate(100, 0.065, 0.0).Reason.ShouldBe(UnavailableReason.Expired);
        ParityForward.FromRate(100, 0.065, double.NaN).Reason.ShouldBe(UnavailableReason.BadInput);
        ForwardSource.ForwardFromRate.Label().ShouldBe("forward_from_rate");
        PriceSource.BsFallback.Label().ShouldBe("bs_fallback");
        PriceSource.VendorBar.Label().ShouldBe("vendor_bar");
        PriceSource.VendorChain.Label().ShouldBe("vendor_chain");
    }

    [Fact]
    public void A_vendor_iv_of_20_is_a_fraction_of_020_with_the_percent_assumed_label()
    {
        var iv = IvUnits.FromVendor(20.0).Value;

        iv.Fraction.ShouldBe(0.20, 1e-15);
        iv.UnitLabel.ShouldBe("percent_assumed");
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-3.0)]
    [InlineData(501.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void A_vendor_iv_that_is_not_a_usable_percent_is_bad_vol_never_zero(double value)
    {
        IvUnits.FromVendor(value).Reason.ShouldBe(UnavailableReason.BadVol);
    }

    [Fact]
    public void Time_to_expiry_runs_to_1530_india_time_over_365_days_across_midnight()
    {
        var expiry = new DateOnly(2026, 11, 12);
        var at = new DateTimeOffset(2026, 11, 11, 23, 0, 0, TimeSpan.FromHours(5.5));

        // 16.5 hours to 15:30 the next day.
        ExpiryTime.YearFraction(at, expiry).Value.ShouldBe(16.5 * 3600.0 / (365.0 * 86400.0), 1e-15);
    }

    [Fact]
    public void Time_to_expiry_reads_the_instant_whatever_the_offset_of_the_clock()
    {
        var expiry = new DateOnly(2026, 11, 12);
        var utc = new DateTimeOffset(2026, 11, 12, 9, 30, 0, TimeSpan.Zero);

        ExpiryTime.YearFraction(utc, expiry).Value.ShouldBe(0.5 * 3600.0 / (365.0 * 86400.0), 1e-15);
    }

    [Theory]
    [InlineData(10, 0, 0, 0)]
    [InlineData(15, 30, 0, 0)]
    [InlineData(15, 30, 1, 0)]
    [InlineData(18, 0, 0, 0)]
    public void Time_at_or_after_1530_on_the_expiry_date_is_expired(int hour, int minute, int second, int dayOffset)
    {
        var at = new DateTimeOffset(2026, 11, 12 + dayOffset, hour, minute, second, TimeSpan.FromHours(5.5));
        var result = ExpiryTime.YearFraction(at, new DateOnly(2026, 11, 12));

        if (hour == 10)
        {
            result.IsAvailable.ShouldBeTrue();
        }
        else
        {
            result.Reason.ShouldBe(UnavailableReason.Expired);
        }
    }
}
