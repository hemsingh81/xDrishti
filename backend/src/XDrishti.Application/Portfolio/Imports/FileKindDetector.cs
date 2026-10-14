namespace XDrishti.Application.Portfolio.Imports;

/// <summary>
/// Tells from the header of a CSV file whether it is a dividend payout file or a tradebook (ADR 0028), so that the trade import and the dividend import can send a file to the import it belongs to instead of refusing it with a missing-column message.
/// A tradebook needs a side column; a dividend file needs a date, a scrip name (or symbol or ISIN) and a paid amount, and has no side column. A header with a date, symbol, quantity and price but no side — and no column that says dividend,
/// payout or paid — is a tradebook without sides. A plain amount column (Amount, Net Amount, Total Amount) makes a dividend list only beside a header that says dividend (dividend, per share, DPS, payout); a date, a name and a plain amount alone are a
/// statement. Pure: it looks at the header names only.
/// </summary>
internal static class FileKindDetector
{
    public const string DividendsCode = "dividends";

    public const string TradesCode = "trades";

    public const string TradebookWithoutSidesCode = "tradebook_without_sides";

    public const string StatementCode = "statement";

    /// <summary>What the owner is told about a tradebook that has no buy/sell column, wherever it is dropped.</summary>
    public const string TradebookWithoutSidesText =
        "This looks like a tradebook, but it has no buy/sell column, so the application cannot tell purchases from sales and cannot read it. "
        + "Export a tradebook that says Buy or Sell for each trade (or add that column), or use Sync trades and ledger on the Portfolio screen to read the history from Dhan.";

    /// <summary>What the owner is told about a file that looks like a statement of amounts, dropped on the dividend import.</summary>
    public const string StatementText =
        "This looks like a statement of amounts, not a dividend list: it has a date, a name and an amount, but nothing that says the amounts are dividends. "
        + "Use a Dividend Paid column (or add a Dividend Per Share column beside the Amount) and try again.";

    /// <summary>What the owner is told about a dividend list dropped on the trade import.</summary>
    public const string DividendsInTradesText = "This looks like a dividend payout list, not a tradebook. Import it with Import dividends in the Cash & dividends panel.";

    public static FileKind Detect(IReadOnlyList<string> header)
    {
        ArgumentNullException.ThrowIfNull(header);
        var trade = Trades.TradeColumns.Probe(header);
        if (trade.Complete)
        {
            return FileKind.Trades;
        }

        var dividend = DividendImport.DividendColumns.Probe(header);
        if (dividend.IsDividendFile && !trade.Side)
        {
            return FileKind.Dividends;
        }

        var tradeShaped = trade.Date && trade.SymbolOrIsin && trade.Qty && trade.Price;
        if (tradeShaped && !trade.Side)
        {
            return FileKind.TradebookWithoutSides;
        }

        return dividend.IsStatement && !trade.Side ? FileKind.Statement : FileKind.Unknown;
    }

    /// <summary>The stable name of a kind as the API reports it (<c>detected</c> of <c>portfolio.wrong_file_kind</c>).</summary>
    public static string Code(FileKind kind) => kind switch
    {
        FileKind.Dividends => DividendsCode,
        FileKind.Trades => TradesCode,
        FileKind.TradebookWithoutSides => TradebookWithoutSidesCode,
        FileKind.Statement => StatementCode,
        _ => "unknown",
    };
}
