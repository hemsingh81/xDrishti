namespace XDrishti.Domain.Options.Pricing;

/// <summary>
/// Black-76 on a forward, the one pricing formula of P14-05: <c>d1 = (ln(F/K) + s^2 T / 2) / (s sqrt(T))</c>, <c>d2 = d1 - s sqrt(T)</c>; call <c>DF (F N(d1) - K N(d2))</c>, put <c>DF (K N(-d2) - F N(-d1))</c>. European exercise only.
/// Prices are rupees as <see cref="double"/>; sigma is a fraction per year; <c>T</c> is calendar years (<see cref="ExpiryTime"/>). Pure and deterministic; an unusable input is an unavailable result, never a throw, 0 or NaN.
/// </summary>
public static class Black76
{
    public static PricedValue<double> Price(OptionKind kind, double forward, double strike, double years, double discount, double sigma)
    {
        if (PricingInputs.Check(forward, strike, years, discount) is { } bad)
        {
            return Priced.Unavailable<double>(bad);
        }

        if (PricingInputs.CheckVol(sigma) is { } badVol)
        {
            return Priced.Unavailable<double>(badVol);
        }

        var price = Core(kind, forward, strike, years, discount, sigma);
        return double.IsFinite(price) ? Priced.Of(price) : Priced.Unavailable<double>(UnavailableReason.BadInput);
    }

    /// <summary>
    /// Delta, gamma, vega and theta in the P14-05 units (vega per volatility point, theta per calendar day). Delta and gamma are per rupee of the spot <c>S</c> with <c>F = S e^((r-q) T)</c>, where <c>r = -ln(DF)/T</c> and <paramref name="dividendYield"/> is q (default 0).
    /// </summary>
    public static PricedValue<OptionGreeks> Greeks(OptionKind kind, double forward, double strike, double years, double discount, double sigma, double dividendYield = 0)
    {
        if (PricingInputs.Check(forward, strike, years, discount) is { } bad)
        {
            return Priced.Unavailable<OptionGreeks>(bad);
        }

        if (PricingInputs.CheckVol(sigma) is { } badVol)
        {
            return Priced.Unavailable<OptionGreeks>(badVol);
        }

        if (!double.IsFinite(dividendYield))
        {
            return Priced.Unavailable<OptionGreeks>(UnavailableReason.BadInput);
        }

        var rate = -Math.Log(discount) / years;
        var spot = forward * Math.Exp(-(rate - dividendYield) * years);
        var carry = Math.Exp(-dividendYield * years);
        var sqrtT = Math.Sqrt(years);
        var sd = sigma * sqrtT;
        var d1 = (Math.Log(forward / strike) + (0.5 * sigma * sigma * years)) / sd;
        var d2 = d1 - sd;
        var pdf = NormalDist.Pdf(d1);
        var decay = -spot * carry * pdf * sigma / (2.0 * sqrtT);
        var isCall = kind == OptionKind.Call;
        var delta = isCall ? carry * NormalDist.Cdf(d1) : -carry * NormalDist.Cdf(-d1);
        var gamma = carry * pdf / (spot * sd);
        var vega = spot * carry * pdf * sqrtT / 100.0;
        var theta = isCall
            ? decay - (rate * strike * discount * NormalDist.Cdf(d2)) + (dividendYield * spot * carry * NormalDist.Cdf(d1))
            : decay + (rate * strike * discount * NormalDist.Cdf(-d2)) - (dividendYield * spot * carry * NormalDist.Cdf(-d1));
        var greeks = new OptionGreeks(delta, gamma, vega, theta / 365.0);
        return double.IsFinite(greeks.Delta) && double.IsFinite(greeks.Gamma) && double.IsFinite(greeks.Vega) && double.IsFinite(greeks.Theta)
            ? Priced.Of(greeks)
            : Priced.Unavailable<OptionGreeks>(UnavailableReason.BadInput);
    }

    /// <summary>Raw vega, the derivative of the price in sigma (not per point). Inputs must already be valid.</summary>
    internal static double RawVega(double forward, double strike, double years, double discount, double sigma)
    {
        var sd = sigma * Math.Sqrt(years);
        var d1 = (Math.Log(forward / strike) + (0.5 * sd * sd)) / sd;
        return discount * forward * NormalDist.Pdf(d1) * Math.Sqrt(years);
    }

    /// <summary>The price for validated inputs, clamped into its no-arbitrage bounds so rounding cannot step outside them.</summary>
    internal static double Core(OptionKind kind, double forward, double strike, double years, double discount, double sigma)
    {
        var sd = sigma * Math.Sqrt(years);
        var d1 = (Math.Log(forward / strike) + (0.5 * sd * sd)) / sd;
        var d2 = d1 - sd;
        double price;
        double intrinsic;
        double upper;
        if (kind == OptionKind.Call)
        {
            price = discount * ((forward * NormalDist.Cdf(d1)) - (strike * NormalDist.Cdf(d2)));
            intrinsic = discount * Math.Max(forward - strike, 0.0);
            upper = discount * forward;
        }
        else
        {
            price = discount * ((strike * NormalDist.Cdf(-d2)) - (forward * NormalDist.Cdf(-d1)));
            intrinsic = discount * Math.Max(strike - forward, 0.0);
            upper = discount * strike;
        }

        return double.IsNaN(price) ? price : Math.Min(Math.Max(price, intrinsic), upper);
    }
}
