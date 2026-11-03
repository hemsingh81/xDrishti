namespace XDrishti.Domain.Options.Pricing;

/// <summary>The one input check of the pricing functions, in the order the spec lists the reasons.</summary>
internal static class PricingInputs
{
    public const double MaxVol = 5.0;

    public static UnavailableReason? Check(double forward, double strike, double years, double discount)
    {
        if (!double.IsFinite(forward) || !double.IsFinite(strike) || !double.IsFinite(years) || forward <= 0 || strike <= 0)
        {
            return UnavailableReason.BadInput;
        }

        if (years <= 0)
        {
            return UnavailableReason.Expired;
        }

        return double.IsFinite(discount) && discount > 0 && discount <= 1 ? null : UnavailableReason.BadRate;
    }

    public static UnavailableReason? CheckVol(double sigma) => double.IsFinite(sigma) && sigma > 0 && sigma <= MaxVol ? null : UnavailableReason.BadVol;
}
