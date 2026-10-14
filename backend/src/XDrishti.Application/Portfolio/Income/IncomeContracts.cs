namespace XDrishti.Application.Portfolio.Income;

/// <summary>
/// What a scope (a holding, a group, an account, the portfolio) received and paid beyond its price return (ADR 0026, P2-14). Money is rupees, percentages are percent numbers (16.1 = 16.1 %), dates are India dates. <c>Dividends</c> are the
/// dividends the owner entered (not voided) and <c>Charges</c> the trade charges of the stored trades plus, for an account and the portfolio only, the ledger's costs of the account as such (DP charges, annual maintenance, delayed-payment interest ...;
/// never the costs of trades, which the trade rows already carry). <c>LedgerCharges</c> is that ledger part of <c>Charges</c> (0 when the account has none; null when the scope does not include account-level items: a holding, a bucket), so the screen can show
/// the ledger's costs apart from the charges of trades. <c>Unallocated</c> is the part of the dividends not allocated to a holding (set for an account and the portfolio only, otherwise null). <c>TotalReturnPct</c> is (realised + unrealised +
/// dividends) over invested, <c>NetTotalReturnPct</c> the same minus the charges; both are null when nothing is invested or the scope has no usable price. <c>NetXirr</c> is the XIRR of the lot cash flows plus the dividends (in) and the charges (out) on
/// their dates, with <c>NetXirrState</c> as for the gross XIRR (<c>ok</c>, <c>insufficient</c>, <c>n_m</c>, <c>no_data</c>, <c>no_price</c>).
/// Only what was entered or read is counted, and the dates say from when each source is known (null: the source has nothing for the scope): <c>DividendsFrom</c> is the first dividend the owner entered, <c>TradeChargesFrom</c> the first stored trade that
/// carries charges (trades read before charges were stored, and CSV or manual trades, carry none — so a trade without charges is "unknown", not "free"), <c>LedgerChargesFrom</c> the first ledger date stored. <c>ChargesFrom</c> is the later of
/// <c>TradeChargesFrom</c> and <c>LedgerChargesFrom</c> when both exist, otherwise the one that does. <c>CoverageFrom</c> is the LATER start of the sources that exist for the scope: the date from which every kind of income that is counted at all is known, so
/// the net figures are never presented as complete from before any of their sources begins. The whole object is null when the scope has no dividend and no charge recorded.
/// </summary>
public sealed record IncomeView(
    decimal Dividends,
    decimal Charges,
    decimal? LedgerCharges,
    decimal? Unallocated,
    decimal? TotalReturnPct,
    decimal? NetTotalReturnPct,
    decimal? NetXirr,
    string NetXirrState,
    DateOnly? CoverageFrom,
    DateOnly? DividendsFrom,
    DateOnly? ChargesFrom,
    DateOnly? TradeChargesFrom,
    DateOnly? LedgerChargesFrom);
