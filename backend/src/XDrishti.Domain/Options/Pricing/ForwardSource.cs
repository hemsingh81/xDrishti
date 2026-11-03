namespace XDrishti.Domain.Options.Pricing;

/// <summary>Where a forward came from: the put-call parity of a two-sided strike, or the rate-only fallback that a caller must label.</summary>
public enum ForwardSource
{
    Parity,
    ForwardFromRate,
}
