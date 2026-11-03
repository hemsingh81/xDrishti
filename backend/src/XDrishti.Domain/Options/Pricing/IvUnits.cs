namespace XDrishti.Domain.Options.Pricing;

/// <summary>Vendor IV units (UNVERIFIED until the P14-02 pilot): the value is read as percent and divided by 100; the label <c>percent_assumed</c> travels with it.</summary>
public static class IvUnits
{
    /// <summary>A vendor IV as a fraction. Not a finite number above zero, or above 500 percent, is <c>bad_vol</c> (never 0).</summary>
    public static PricedValue<VendorVol> FromVendor(double value)
    {
        if (!double.IsFinite(value) || value <= 0 || value > PricingInputs.MaxVol * 100.0)
        {
            return Priced.Unavailable<VendorVol>(UnavailableReason.BadVol);
        }

        return Priced.Of(new VendorVol(value / 100.0, PricingLabels.IvUnitPercentAssumed));
    }
}
