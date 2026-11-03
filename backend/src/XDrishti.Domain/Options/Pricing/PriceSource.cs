namespace XDrishti.Domain.Options.Pricing;

/// <summary>Where a premium came from. Other slices carry the label; this core never decides to use <see cref="BsFallback"/> (the caller counts it).</summary>
public enum PriceSource
{
    VendorBar,
    VendorChain,
    BsFallback,
}
