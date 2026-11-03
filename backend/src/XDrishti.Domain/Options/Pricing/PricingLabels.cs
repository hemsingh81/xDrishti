namespace XDrishti.Domain.Options.Pricing;

/// <summary>The stored snake_case words of the pricing enums (the labels other slices write next to a value).</summary>
public static class PricingLabels
{
    /// <summary>The unit label that travels with every vendor IV until the P14-02 pilot confirms it.</summary>
    public const string IvUnitPercentAssumed = "percent_assumed";

    public static string Label(this UnavailableReason reason) => reason switch
    {
        UnavailableReason.BadInput => "bad_input",
        UnavailableReason.BadVol => "bad_vol",
        UnavailableReason.Expired => "expired",
        UnavailableReason.BadRate => "bad_rate",
        UnavailableReason.BadPrice => "bad_price",
        UnavailableReason.BelowIntrinsic => "below_intrinsic",
        UnavailableReason.AboveUpperBound => "above_upper_bound",
        UnavailableReason.NoConvergence => "no_convergence",
        UnavailableReason.VolNotIdentified => "vol_not_identified",
        UnavailableReason.NoTwoSidedQuote => "no_two_sided_quote",
        _ => "bad_input",
    };

    public static string Label(this PriceSource source) => source switch
    {
        PriceSource.VendorBar => "vendor_bar",
        PriceSource.VendorChain => "vendor_chain",
        PriceSource.BsFallback => "bs_fallback",
        _ => "bs_fallback",
    };

    public static string Label(this ForwardSource source) => source switch
    {
        ForwardSource.Parity => "parity",
        ForwardSource.ForwardFromRate => "forward_from_rate",
        _ => "forward_from_rate",
    };
}
