namespace XDrishti.Domain.Options.Pricing;

/// <summary>Black-Scholes as the spot form of <see cref="Black76"/>: <c>F = S e^((r-q) T)</c>, <c>DF = e^(-r T)</c>, <c>q</c> = 0 unless given. A negative rate is <c>bad_rate</c> (the discount factor must be in (0, 1]).</summary>
public static class BlackScholes
{
    public static PricedValue<double> Price(OptionKind kind, double spot, double strike, double years, double rate, double sigma, double dividendYield = 0)
    {
        var inputs = Inputs(spot, years, rate, dividendYield);
        return inputs.TryGet(out var parts)
            ? Black76.Price(kind, parts.Forward, strike, years, parts.Discount, sigma)
            : Priced.Unavailable<double>(inputs.Reason!.Value);
    }

    public static PricedValue<OptionGreeks> Greeks(OptionKind kind, double spot, double strike, double years, double rate, double sigma, double dividendYield = 0)
    {
        var inputs = Inputs(spot, years, rate, dividendYield);
        return inputs.TryGet(out var parts)
            ? Black76.Greeks(kind, parts.Forward, strike, years, parts.Discount, sigma, dividendYield)
            : Priced.Unavailable<OptionGreeks>(inputs.Reason!.Value);
    }

    private static PricedValue<(double Forward, double Discount)> Inputs(double spot, double years, double rate, double dividendYield)
    {
        if (!double.IsFinite(spot) || spot <= 0 || !double.IsFinite(years) || !double.IsFinite(dividendYield))
        {
            return Priced.Unavailable<(double, double)>(UnavailableReason.BadInput);
        }

        if (years <= 0)
        {
            return Priced.Unavailable<(double, double)>(UnavailableReason.Expired);
        }

        if (!double.IsFinite(rate) || rate < 0)
        {
            return Priced.Unavailable<(double, double)>(UnavailableReason.BadRate);
        }

        var forward = spot * Math.Exp((rate - dividendYield) * years);
        return double.IsFinite(forward)
            ? Priced.Of((forward, Math.Exp(-rate * years)))
            : Priced.Unavailable<(double, double)>(UnavailableReason.BadInput);
    }
}
