namespace XDrishti.Domain.Reporting;

/// <summary>The default index of the basket profile and ranking (BK-06c): the master-list Nifty 500. The portfolio benchmark (Nifty 50) is a separate setting and is not touched.</summary>
public static class BasketBenchmarkDefault
{
    /// <summary>The canonical symbol the master gives the index: <c>CanonicalSymbol.ForIndex("NIFTY 500")</c>.</summary>
    public const string CanonicalSymbol = "NSE:NIFTY_500";

    /// <summary>The display name the index is also found by (case-insensitive) when the symbol differs.</summary>
    public const string DisplayName = "Nifty 500";
}
