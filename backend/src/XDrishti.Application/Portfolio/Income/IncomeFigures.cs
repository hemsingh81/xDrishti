using XDrishti.Application.Portfolio.Returns;

namespace XDrishti.Application.Portfolio.Income;

/// <summary>
/// The income figures of a scope (see <see cref="IncomeView"/> for the meaning). <see cref="Unallocated"/> and <see cref="LedgerCharges"/> are null when account-level items do not belong to the scope. <see cref="NetXirr"/> carries the state; its rate is a fraction per year.
/// </summary>
internal sealed record IncomeFigures(
    decimal Dividends,
    decimal Charges,
    decimal? LedgerCharges,
    decimal? Unallocated,
    decimal? TotalReturnPct,
    decimal? NetTotalReturnPct,
    XirrResult NetXirr,
    DateOnly? CoverageFrom,
    DateOnly? DividendsFrom,
    DateOnly? ChargesFrom,
    DateOnly? TradeChargesFrom,
    DateOnly? LedgerChargesFrom);
