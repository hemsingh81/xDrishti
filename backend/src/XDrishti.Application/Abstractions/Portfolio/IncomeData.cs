namespace XDrishti.Application.Abstractions.Portfolio;

/// <summary>
/// The accounts an income read covers, chosen like the holdings of <see cref="HoldingFilter"/>: one account (<see cref="AccountId"/>), or without one every active account with the Portfolio role, and with <see cref="TotalsOnly"/> only those
/// that count in totals. <see cref="Holdings"/> are the keys of the holdings of those accounts when the caller has them already (every holding of every account of the scope, whatever its status, bucket or text — not a filtered list): the read then does not
/// read them again; null: the read reads them. With <see cref="AccountLevel"/> false the caller wants the figures of holdings only, so the ledger (its costs and its first date) is not read.
/// </summary>
public sealed record IncomeScope(Guid? AccountId, bool TotalsOnly, IReadOnlyList<IncomeHolding>? Holdings = null, bool AccountLevel = true);

/// <summary>The key of a holding: what a trade's ISIN and symbol are matched against. <see cref="Isin"/> and <see cref="TradingSymbol"/> are as stored.</summary>
public sealed record IncomeHolding(Guid Id, Guid AccountId, string? Isin, string TradingSymbol, bool IsOpen);

/// <summary>A dividend that is not voided: its account, holding (null: unallocated), India pay date and rupees.</summary>
public sealed record IncomeDividend(Guid AccountId, Guid? HoldingId, DateOnly PayDate, decimal Amount);

/// <summary>The charges of the trades (not voided) of one security of an account on one India day, rupees (above 0).</summary>
public sealed record IncomeTradeCharge(Guid AccountId, string? Isin, string TradingSymbol, DateOnly TradeDate, decimal Amount);

/// <summary>
/// The ledger entries of class <c>charges</c> of one account on one day with the same texts, summed with the sign turned (positive: paid, negative: refunded). The texts are Dhan's (untrusted); which of these are costs of the account
/// and not of trades is decided by the income figures, not by the read.
/// </summary>
public sealed record IncomeLedgerCharge(Guid AccountId, DateOnly Date, string Description, string Narration, decimal Amount);

/// <summary>The earliest date of any ledger entry stored for an account: the ledger's costs are known from this date on.</summary>
public sealed record IncomeLedgerStart(Guid AccountId, DateOnly From);

/// <summary>What the income figures of a scope are made of (see <see cref="IIncomeQueries"/>).</summary>
public sealed record IncomeData(
    IReadOnlyList<Guid> AccountIds,
    IReadOnlyList<IncomeHolding> Holdings,
    IReadOnlyList<IncomeDividend> Dividends,
    IReadOnlyList<IncomeTradeCharge> TradeCharges,
    IReadOnlyList<IncomeLedgerCharge> LedgerCharges,
    IReadOnlyList<IncomeLedgerStart> LedgerStarts)
{
    public static IncomeData None { get; } = new([], [], [], [], [], []);
}
