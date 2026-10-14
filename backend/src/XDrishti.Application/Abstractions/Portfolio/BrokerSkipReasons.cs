namespace XDrishti.Application.Abstractions.Portfolio;

/// <summary>Why a row of Dhan's trade history is not stored (counted per reason in the run): not an NSE equity trade, or not a delivery trade.</summary>
public static class BrokerSkipReasons
{
    public const string Derivatives = "derivatives";

    public const string Commodity = "commodity";

    public const string Currency = "currency";

    public const string Bse = "bse";

    public const string NonEquity = "non_equity";

    /// <summary>An intraday, cover or bracket order: it nets out within the day and never reaches the holdings.</summary>
    public const string Intraday = "intraday";

    /// <summary>A row with no trading symbol whose ISIN and Dhan security id could not be matched to a share either: it cannot be told which security it is, so it is not stored.</summary>
    public const string NoSymbol = "no_symbol";
}
