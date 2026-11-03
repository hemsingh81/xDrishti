namespace XDrishti.Domain.Options.Iv;

/// <summary>The labels that travel with the <c>iv-1</c> features (P14-11).</summary>
public static class IvLabels
{
    /// <summary>The unit the vendor IV is read in: percent, divided by 100 (UNVERIFIED until the P14-02 pilot; a wrong unit scales every value but not the ranks). The same word as the pricing core's label; a test pins the two together.</summary>
    public const string Unit = "percent_assumed";

    /// <summary>The largest vendor IV read as a value, in percent (the pricing core's bound: above 500 percent is a bad vol).</summary>
    public const double MaxPercent = 500.0;
}
