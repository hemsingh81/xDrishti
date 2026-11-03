namespace XDrishti.Application.Market.Eod;

/// <summary>The logical fields of a daily row.</summary>
internal enum EodField
{
    Date,
    Symbol,
    Series,
    Isin,
    Open,
    High,
    Low,
    Close,
    PrevClose,
    TradedQty,
    Turnover,
    Trades,
}

/// <summary>One known header layout: its name and the header text of each logical field. A file is accepted only when every non-optional name is in its header.</summary>
internal sealed record EodLayout(string Name, IReadOnlyDictionary<EodField, string> Columns, IReadOnlySet<EodField> Optional);

/// <summary>
/// The one place that knows NSE's file names, URL patterns and column names (CF-04 Part A). <b>UNVERIFIED until the probe has run</b> (<c>xd data eod-probe</c>): these are the best-known layouts and are to be verified by the probe
/// against the raw files it stores under <c>data/</c>. A header outside this table makes the parser refuse the file (<c>history.unknown_format</c>); nothing is guessed.
/// </summary>
internal static class EodFormats
{
    public static readonly EodLayout Legacy = new(
        "legacy",
        new Dictionary<EodField, string>
        {
            [EodField.Date] = "TIMESTAMP",
            [EodField.Symbol] = "SYMBOL",
            [EodField.Series] = "SERIES",
            [EodField.Isin] = "ISIN",
            [EodField.Open] = "OPEN",
            [EodField.High] = "HIGH",
            [EodField.Low] = "LOW",
            [EodField.Close] = "CLOSE",
            [EodField.PrevClose] = "PREVCLOSE",
            [EodField.TradedQty] = "TOTTRDQTY",
            [EodField.Turnover] = "TOTTRDVAL",
            [EodField.Trades] = "TOTALTRADES",
        },
        new HashSet<EodField> { EodField.Isin, EodField.PrevClose, EodField.Trades });

    public static readonly EodLayout Udiff = new(
        "udiff",
        new Dictionary<EodField, string>
        {
            [EodField.Date] = "TradDt",
            [EodField.Symbol] = "TckrSymb",
            [EodField.Series] = "SctySrs",
            [EodField.Isin] = "ISIN",
            [EodField.Open] = "OpnPric",
            [EodField.High] = "HghPric",
            [EodField.Low] = "LwPric",
            [EodField.Close] = "ClsPric",
            [EodField.PrevClose] = "PrvsClsgPric",
            [EodField.TradedQty] = "TtlTradgVol",
            [EodField.Turnover] = "TtlTrfVal",
            [EodField.Trades] = "TtlNbOfTxsExctd",
        },
        new HashSet<EodField> { EodField.Isin, EodField.PrevClose, EodField.Trades });

    public static readonly IReadOnlyList<EodLayout> Layouts = [Legacy, Udiff];

    /// <summary>Delivery file (MTO) header prefixes, lower case. UNVERIFIED until the probe has run.</summary>
    public static readonly (string Record, string Symbol, string Series, string Traded, string Delivered, string Percent) DeliveryHeader =
        ("record type", "name of security", "type of security", "quantity traded", "deliverable quantity", "% of deliverable");

    /// <summary>The corporate-actions file (read from a local file): header names, UNVERIFIED until the probe has run.</summary>
    public static readonly (string Symbol, string Series, string Purpose, string ExDate) ActionsHeader = ("symbol", "series", "purpose", "ex-date");
}
