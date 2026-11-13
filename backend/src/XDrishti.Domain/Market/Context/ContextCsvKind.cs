namespace XDrishti.Domain.Market.Context;

/// <summary>Which index a dropped NSE daily CSV holds (P3-01d, fallback D).</summary>
public enum ContextCsvKind
{
    /// <summary>NIFTY 50 history (niftyindices.com): date, open, high, low, close.</summary>
    Index,

    /// <summary>INDIA VIX history (nseindia.com): date, open, high, low, close, prev. close, change, % change.</summary>
    Vix,
}
