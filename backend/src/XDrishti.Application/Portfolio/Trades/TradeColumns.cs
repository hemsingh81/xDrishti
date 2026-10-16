using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.Trades;

/// <summary>
/// Which column of a tradebook file is which, found by the header names (ADR 0023): case and punctuation do not matter (<c>Trade Date</c>, <c>trade_date</c> and <c>TRADEDATE</c> are one name). Unknown columns are ignored. The aliases of a column are in
/// order of preference. Charges: a column that holds the total is used as it is; only without one are the components (brokerage, taxes, stamp duty, …) summed, never both.
/// </summary>
internal sealed class TradeColumns
{
    private static readonly string[] DateAliases = ["tradedate", "date", "tradedt", "transactiondate", "tradingdate", "executiondate", "orderexecutiontime", "orderexecutiondatetime", "executiontime", "tradedatetime", "datetime", "timestamp"];

    private static readonly string[] TimeAliases = ["tradetime", "time", "exchangetime", "orderexecutiontime", "executiontime", "orderexecutiondatetime", "tradedatetime", "datetime", "timestamp"];

    private static readonly string[] SymbolAliases = ["symbol", "tradingsymbol", "tsym", "scrip", "scripname", "scripsymbol", "instrument", "instrumentname", "security", "securityname", "stock", "stockname", "stocksymbol"];

    private static readonly string[] IsinAliases = ["isin", "isincode", "isinno"];

    private static readonly string[] SideAliases = ["tradetype", "side", "buysell", "buyorsell", "transactiontype", "action", "bs"];

    private static readonly string[] QtyAliases = ["quantity", "qty", "tradedquantity", "tradedqty", "quantitytraded", "noofshares", "shares", "units"];

    private static readonly string[] PriceAliases = ["price", "tradeprice", "tradedprice", "rate", "avgprice", "averageprice", "executionprice", "unitprice"];

    private static readonly string[] ExchangeAliases = ["exchange", "exch", "exchangename"];

    private static readonly string[] TradeIdAliases = ["tradeid", "tradeno", "tradenumber", "tradenum", "exchangetradeid"];

    private static readonly string[] OrderIdAliases = ["orderid", "orderno", "ordernumber", "exchangeorderid"];

    private static readonly string[] SegmentAliases = ["segment", "exchangesegment", "marketsegment", "instrumenttype", "assettype"];

    private static readonly string[] ChargesTotalAliases = ["totalcharges", "totalcharge", "charges", "totalfees", "totalfee", "fees"];

    private static readonly string[] ChargeComponentAliases =
    [
        "brokerage", "brokeragecharges", "stt", "securitiestransactiontax", "sttctt", "stampduty", "stampdutycharges", "transactioncharges", "exchangetransactioncharges",
        "exchangecharges", "gst", "sebicharges", "sebiturnoverfees", "sebifees", "ipft", "ipftcharges", "dpcharges", "clearingcharges", "taxes", "tax", "othercharges", "otherlevies", "levies",
    ];

    /// <summary>The names of the columns the file must have (one of symbol or ISIN), for the refusal message.</summary>
    private static readonly (string Label, string[] Aliases)[] Required =
    [
        ("date", DateAliases),
        ("symbol or ISIN", [.. SymbolAliases, .. IsinAliases]),
        ("side", SideAliases),
        ("quantity", QtyAliases),
        ("price", PriceAliases),
    ];

    private TradeColumns()
    {
    }

    public int Date { get; private init; } = -1;

    public int Time { get; private init; } = -1;

    public int Symbol { get; private init; } = -1;

    public int Isin { get; private init; } = -1;

    public int Side { get; private init; } = -1;

    public int Qty { get; private init; } = -1;

    public int Price { get; private init; } = -1;

    public int Exchange { get; private init; } = -1;

    public int TradeId { get; private init; } = -1;

    public int OrderId { get; private init; } = -1;

    public int Segment { get; private init; } = -1;

    public int ChargesTotal { get; private init; } = -1;

    public IReadOnlyList<int> ChargeParts { get; private init; } = [];

    /// <summary>Which of the parts of a tradebook the header has, without reading the rest (the file-kind detector uses it).</summary>
    public static TradeColumnProbe Probe(IReadOnlyList<string> header)
    {
        ArgumentNullException.ThrowIfNull(header);
        var names = header.Select(Normalise).ToArray();
        return new TradeColumnProbe(
            Pick(names, DateAliases) >= 0,
            Pick(names, SymbolAliases) >= 0 || Pick(names, IsinAliases) >= 0,
            Pick(names, SideAliases) >= 0,
            Pick(names, QtyAliases) >= 0,
            Pick(names, PriceAliases) >= 0);
    }

    /// <summary>The name of a header cell as the aliases are written: lower case letters and digits only.</summary>
    public static string Normalise(string? header)
    {
        var chars = (header ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray();
        return new string(chars);
    }

    /// <summary>Finds the columns. Failure (<c>portfolio.invalid</c>, field <c>file</c>) when a required column is missing, naming what is missing and the accepted names.</summary>
    public static Result<TradeColumns> Find(IReadOnlyList<string> header)
    {
        ArgumentNullException.ThrowIfNull(header);
        var names = header.Select(Normalise).ToArray();
        var date = Pick(names, DateAliases);
        var symbol = Pick(names, SymbolAliases);
        var isin = Pick(names, IsinAliases);
        var side = Pick(names, SideAliases);
        var qty = Pick(names, QtyAliases);
        var price = Pick(names, PriceAliases);
        var missing = new List<string>();
        if (date < 0)
        {
            missing.Add(Required[0].Label);
        }

        if (symbol < 0 && isin < 0)
        {
            missing.Add(Required[1].Label);
        }

        if (side < 0)
        {
            missing.Add(Required[2].Label);
        }

        if (qty < 0)
        {
            missing.Add(Required[3].Label);
        }

        if (price < 0)
        {
            missing.Add(Required[4].Label);
        }

        if (missing.Count > 0)
        {
            var accepted = Required.Where(r => missing.Contains(r.Label)).Select(r => $"{r.Label}: {string.Join(", ", r.Aliases.Take(6))}");
            return Result.Failure<TradeColumns>(PortfolioErrors.Invalid(
                "file",
                $"The file has no column for {string.Join(", ", missing)}. Accepted header names (any case, punctuation ignored): {string.Join("; ", accepted)}. Download the template for the exact layout."));
        }

        var total = Pick(names, ChargesTotalAliases);
        var parts = total >= 0
            ? []
            : names.Select((name, index) => (name, index)).Where(x => ChargeComponentAliases.Contains(x.name, StringComparer.Ordinal)).Select(x => x.index).ToArray();
        var tradeId = Pick(names, TradeIdAliases);
        return Result.Success(new TradeColumns
        {
            Date = date,
            Time = Pick(names, TimeAliases),
            Symbol = symbol,
            Isin = isin,
            Side = side,
            Qty = qty,
            Price = price,
            Exchange = Pick(names, ExchangeAliases),
            TradeId = tradeId,
            OrderId = Pick(names, OrderIdAliases),
            Segment = Pick(names, SegmentAliases),
            ChargesTotal = total,
            ChargeParts = parts,
        });
    }

    private static int Pick(string[] names, string[] aliases)
    {
        foreach (var alias in aliases)
        {
            var index = Array.IndexOf(names, alias);
            if (index >= 0)
            {
                return index;
            }
        }

        return -1;
    }
}

/// <summary>Which parts of a tradebook a header has: a date, a symbol or ISIN, a side, a quantity and a price.</summary>
internal readonly record struct TradeColumnProbe(bool Date, bool SymbolOrIsin, bool Side, bool Qty, bool Price)
{
    /// <summary>Every required part is there: the file is a tradebook the trade reader can read.</summary>
    public bool Complete => Date && SymbolOrIsin && Side && Qty && Price;
}
