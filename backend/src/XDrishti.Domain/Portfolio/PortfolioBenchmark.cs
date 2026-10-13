namespace XDrishti.Domain.Portfolio;

/// <summary>The default benchmark of the portfolio (ADR 0020 §2): the master-list index Nifty 50. The choice is stored in the settings; this is only what applies until the owner picks another index.</summary>
public static class PortfolioBenchmark
{
    /// <summary>The canonical symbol the Dhan master gives the index (<c>NSE:NIFTY</c>, display name "Nifty 50").</summary>
    public const string DefaultCanonicalSymbol = "NSE:NIFTY";

    /// <summary>The display name the default index is also found by when the symbol differs.</summary>
    public const string DefaultDisplayName = "Nifty 50";
}
