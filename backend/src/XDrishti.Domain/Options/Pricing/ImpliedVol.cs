namespace XDrishti.Domain.Options.Pricing;

/// <summary>
/// The implied-volatility solver of P14-05: a safeguarded Newton on Black-76. Bracket sigma in [1e-4, 5.0]; start at <c>sqrt(2 |ln(F/K)| / T)</c> clipped into the bracket (0.2 when that is 0); bisect whenever Newton leaves the bracket or vega is below 1e-12;
/// stop when <c>|price(s) - price| &lt;= 1e-10 max(1, price)</c> or the bracket is narrower than 1e-12; at most 100 iterations. A converged root gets up to three guarded polishing Newton steps (while the step exceeds 1e-13 and the residual does not grow) so the volatility error is far below the price tolerance divided by vega.
/// A root whose raw vega (the derivative in sigma) is below <c>1e-8 F</c> is <c>vol_not_identified</c>, never a clipped number. Deterministic; no shared state.
/// </summary>
public static class ImpliedVol
{
    public const double LowVol = 1e-4;
    public const double HighVol = PricingInputs.MaxVol;
    public const int MaxIterations = 100;
    private const double PriceTolerance = 1e-10;
    private const double BracketTolerance = 1e-12;
    private const double VegaFloor = 1e-12;
    private const double IdentifiedVegaFactor = 1e-8;
    private const double IntrinsicSlack = 1e-12;

    public static PricedValue<double> Solve(OptionKind kind, double price, double forward, double strike, double years, double discount)
    {
        if (PricingInputs.Check(forward, strike, years, discount) is { } bad)
        {
            return Priced.Unavailable<double>(bad);
        }

        if (!double.IsFinite(price) || price <= 0)
        {
            return Priced.Unavailable<double>(UnavailableReason.BadPrice);
        }

        var isCall = kind == OptionKind.Call;
        var intrinsic = discount * Math.Max(isCall ? forward - strike : strike - forward, 0.0);
        var upper = discount * (isCall ? forward : strike);
        if (price <= intrinsic + IntrinsicSlack)
        {
            return Priced.Unavailable<double>(UnavailableReason.BelowIntrinsic);
        }

        if (price >= upper)
        {
            return Priced.Unavailable<double>(UnavailableReason.AboveUpperBound);
        }

        var lo = LowVol;
        var hi = HighVol;
        if (Black76.Core(kind, forward, strike, years, discount, lo) > price || Black76.Core(kind, forward, strike, years, discount, hi) < price)
        {
            return Priced.Unavailable<double>(UnavailableReason.NoConvergence);
        }

        var start = Math.Sqrt(2.0 * Math.Abs(Math.Log(forward / strike)) / years);
        var sigma = start > 0 ? Math.Min(Math.Max(start, lo), hi) : 0.2;
        var tolerance = PriceTolerance * Math.Max(1.0, price);
        for (var i = 0; i < MaxIterations; i++)
        {
            var diff = Black76.Core(kind, forward, strike, years, discount, sigma) - price;
            if (double.IsNaN(diff))
            {
                return Priced.Unavailable<double>(UnavailableReason.NoConvergence);
            }

            if (Math.Abs(diff) <= tolerance)
            {
                return Finish(forward, strike, years, discount, Polish(kind, price, forward, strike, years, discount, sigma, diff));
            }

            if (diff < 0)
            {
                lo = sigma;
            }
            else
            {
                hi = sigma;
            }

            if (hi - lo < BracketTolerance)
            {
                return Finish(forward, strike, years, discount, 0.5 * (lo + hi));
            }

            var vega = Black76.RawVega(forward, strike, years, discount, sigma);
            var next = vega < VegaFloor ? double.NaN : sigma - (diff / vega);
            sigma = next > lo && next < hi ? next : 0.5 * (lo + hi);
        }

        return Priced.Unavailable<double>(UnavailableReason.NoConvergence);
    }

    private static double Polish(OptionKind kind, double price, double forward, double strike, double years, double discount, double sigma, double diff)
    {
        for (var i = 0; i < 3; i++)
        {
            var vega = Black76.RawVega(forward, strike, years, discount, sigma);
            if (vega < VegaFloor)
            {
                return sigma;
            }

            var step = diff / vega;
            var next = sigma - step;
            if (!(next >= LowVol && next <= HighVol))
            {
                return sigma;
            }

            var nextDiff = Black76.Core(kind, forward, strike, years, discount, next) - price;
            if (!(Math.Abs(nextDiff) <= Math.Abs(diff)))
            {
                return sigma;
            }

            sigma = next;
            diff = nextDiff;
            if (Math.Abs(step) <= 1e-13)
            {
                break;
            }
        }

        return sigma;
    }

    private static PricedValue<double> Finish(double forward, double strike, double years, double discount, double sigma) =>
        Black76.RawVega(forward, strike, years, discount, sigma) < IdentifiedVegaFactor * forward
            ? Priced.Unavailable<double>(UnavailableReason.VolNotIdentified)
            : Priced.Of(sigma);
}
