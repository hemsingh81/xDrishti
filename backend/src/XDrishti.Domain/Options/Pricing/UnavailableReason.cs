namespace XDrishti.Domain.Options.Pricing;

/// <summary>The closed list of reasons a pricing value is unavailable (spec P14-05). The first member is the value of an uninitialised result, so a <c>default</c> result is never a usable number.</summary>
public enum UnavailableReason
{
    BadInput,
    BadVol,
    Expired,
    BadRate,
    BadPrice,
    BelowIntrinsic,
    AboveUpperBound,
    NoConvergence,
    VolNotIdentified,
    NoTwoSidedQuote,
}
