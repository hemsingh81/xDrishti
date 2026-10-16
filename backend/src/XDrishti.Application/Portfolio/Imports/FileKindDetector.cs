namespace XDrishti.Application.Portfolio.Imports;

/// <summary>
/// Tells from the header of a CSV file whether it is a dividend payout file or a tradebook (ADR 0028), so that the trade import and the dividend import can send a file to the import it belongs to instead of refusing it with a missing-column message.
/// A tradebook needs a side column; a dividend file needs a date, a scrip name (or symbol or ISIN) and a paid amount, and has no side column. A header with a date, symbol, quantity and price but no side — and no column that says dividend,
/// payout or paid — is a tradebook without sides. A plain amount column (Amount, Net Amount, Total Amount) makes a dividend list only beside a header that says dividend (dividend, per share, DPS, payout); a date, a name and a plain amount alone are a
/// statement. Dhan's downloaded reports (ADR 0029) start with a title line and some personal lines before the header, in CSV, <c>.xls</c> or <c>.xlsx</c>: <see cref="DetectSheet"/> judges them from the leading rows — the title line first, else the first
/// row that has the header names of one of the reports. Pure: it looks at the title and header names only, never at the personal lines.
/// </summary>
internal static class FileKindDetector
{
    public const string DividendsCode = "dividends";

    public const string TradesCode = "trades";

    public const string TradebookWithoutSidesCode = "tradebook_without_sides";

    public const string StatementCode = "statement";

    public const string GlobalTransactionReportCode = "global_transaction_report";

    public const string DpChargesCode = "dp_charges";

    public const string DematHoldingCode = "demat_holding";

    public const string PnlReportCode = "pnl_report";

    /// <summary>How many leading rows of a report are looked at to find its title and header (the personal lines come between).</summary>
    public const int LeadingRows = 20;

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

    /// <summary>What the owner is told about a report that has an import of its own, dropped on the tradebook or dividend import.</summary>
    public static string ReportElsewhereText(FileKind kind) => kind switch
    {
        FileKind.GlobalTransactionReport => "This is Dhan's global transaction report, which holds trades with their charges. Import it with Import on the Portfolio screen, which reads reports of every kind.",
        FileKind.DpCharges => "This is Dhan's depository (DP) charges sheet, which holds account costs, not trades. Import it with Import on the Portfolio screen, which reads reports of every kind.",
        FileKind.DematHolding => "This is Dhan's demat holding statement, which checks your holdings and teaches security names. Import it with Import on the Portfolio screen, which reads reports of every kind.",
        FileKind.PnlReport => PnlReportText,
        _ => string.Empty,
    };

    /// <summary>What the owner is told about a profit and loss statement, wherever it is dropped.</summary>
    public const string PnlReportText = "This is a profit and loss statement from Dhan. It is a statement of results, not a list of trades, so it is not imported.";

    /// <summary>True for the kinds of Dhan's downloaded reports that begin with a title and personal lines (ADR 0029).</summary>
    public static bool IsReport(FileKind kind) => kind is FileKind.GlobalTransactionReport or FileKind.DpCharges or FileKind.DematHolding or FileKind.PnlReport;

    /// <summary>
    /// The kind of a report from its leading rows (title line, personal lines, header): the title line when it is one of Dhan's, else the first row that has the header names of one of the reports, else the first row judged as a tradebook or dividend header
    /// (<see cref="Detect"/>). Blank rows are skipped. Only the title and the header names are looked at.
    /// </summary>
    public static FileKind DetectSheet(IEnumerable<IReadOnlyList<string>> leadingRows)
    {
        ArgumentNullException.ThrowIfNull(leadingRows);
        var rows = leadingRows.Where(r => r.Any(c => !string.IsNullOrWhiteSpace(c))).Take(LeadingRows).ToList();
        if (rows.Count == 0)
        {
            return FileKind.Unknown;
        }

        var titled = DetectTitle(rows[0]);
        if (titled != FileKind.Unknown)
        {
            return titled;
        }

        foreach (var row in rows)
        {
            var kind = DetectReportHeader(row);
            if (kind != FileKind.Unknown)
            {
                return kind;
            }
        }

        return Detect(rows[0]);
    }

    /// <summary>The kind a title line names (<c>Global transction report,From … to …</c> - sic - <c>Holding summary,For …</c>, <c>DP Charges | From …</c>, <c>Realised PnL Report,…</c>, <c>PnL report,…</c>); Unknown for any other first row.</summary>
    public static FileKind DetectTitle(IReadOnlyList<string> firstRow)
    {
        ArgumentNullException.ThrowIfNull(firstRow);
        var title = firstRow.Select(Trades.TradeColumns.Normalise).FirstOrDefault(c => c.Length > 0) ?? string.Empty;
        if (title.StartsWith("globaltransctionreport", StringComparison.Ordinal) || title.StartsWith("globaltransactionreport", StringComparison.Ordinal))
        {
            return FileKind.GlobalTransactionReport;
        }

        if (title.StartsWith("dpcharges", StringComparison.Ordinal))
        {
            return FileKind.DpCharges;
        }

        if (title.StartsWith("holdingsummary", StringComparison.Ordinal))
        {
            return FileKind.DematHolding;
        }

        return title.StartsWith("realisedpnlreport", StringComparison.Ordinal) || title.StartsWith("pnlreport", StringComparison.Ordinal) ? FileKind.PnlReport : FileKind.Unknown;
    }

    /// <summary>The report whose header names a row has (a file whose title line was cut off still has its header); Unknown when it has none.</summary>
    public static FileKind DetectReportHeader(IReadOnlyList<string> row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var names = row.Select(Trades.TradeColumns.Normalise).Where(n => n.Length > 0).ToHashSet(StringComparer.Ordinal);
        if (names.Contains("scripname") && names.Contains("billno") && names.Contains("buyvalue") && names.Contains("sellvalue"))
        {
            return FileKind.GlobalTransactionReport;
        }

        if (names.Contains("isin") && names.Contains("securityname") && names.Contains("typeoftransaction") && names.Contains("charges"))
        {
            return FileKind.DpCharges;
        }

        if (names.Contains("scripname") && names.Contains("isincode") && names.Contains("freeholding") && names.Contains("closingprice"))
        {
            return FileKind.DematHolding;
        }

        return names.Contains("scripname") && names.Contains("realisedpl") ? FileKind.PnlReport : FileKind.Unknown;
    }

    /// <summary>The stable name of a kind as the API reports it (<c>detected</c> of <c>portfolio.wrong_file_kind</c>).</summary>
    public static string Code(FileKind kind) => kind switch
    {
        FileKind.Dividends => DividendsCode,
        FileKind.Trades => TradesCode,
        FileKind.TradebookWithoutSides => TradebookWithoutSidesCode,
        FileKind.Statement => StatementCode,
        FileKind.GlobalTransactionReport => GlobalTransactionReportCode,
        FileKind.DpCharges => DpChargesCode,
        FileKind.DematHolding => DematHoldingCode,
        FileKind.PnlReport => PnlReportCode,
        _ => "unknown",
    };
}
