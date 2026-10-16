namespace XDrishti.Application.Portfolio.Imports;

/// <summary>What a downloaded file is, judged from its title line and header alone (ADR 0028, 0029).</summary>
internal enum FileKind
{
    /// <summary>Neither of the kinds below.</summary>
    Unknown,

    /// <summary>A dividend payout file: a date, a scrip name (or symbol or ISIN) and a paid amount.</summary>
    Dividends,

    /// <summary>A tradebook with a buy/sell column.</summary>
    Trades,

    /// <summary>Looks like a tradebook (date, symbol, quantity, price) but has no buy/sell column, so it cannot be read as one.</summary>
    TradebookWithoutSides,

    /// <summary>A date, a name and a plain amount (Amount, Net Amount), but nothing that says the amounts are dividends: a statement of some kind, not a dividend list.</summary>
    Statement,

    /// <summary>Dhan's global transaction report: one row per bill, scrip and day with buy and sell quantity and value and every charge.</summary>
    GlobalTransactionReport,

    /// <summary>Dhan's depository-charges sheet: date, ISIN, security, quantity, buy/sell, type of transaction and the charge.</summary>
    DpCharges,

    /// <summary>Dhan's demat holding statement: one row per security with its ISIN and the depository's quantity.</summary>
    DematHolding,

    /// <summary>Dhan's realised P&amp;L or P&amp;L statement: a statement of results, not trades.</summary>
    PnlReport,
}
