using XDrishti.Application.Portfolio.Returns;

namespace XDrishti.Application.Portfolio.Income;

/// <summary>The dividends (money in, positive) and the charges (money paid, positive; a refund is negative) recorded for one holding, by date, oldest first.</summary>
internal sealed record HoldingIncome(IReadOnlyList<CashFlow> Dividends, IReadOnlyList<CashFlow> Charges)
{
    public static HoldingIncome None { get; } = new([], []);
}

/// <summary>
/// What belongs to an account and to none of its holdings, by date, oldest first: the dividends not allocated to a holding, the charges of trades whose security is none of its held holdings (<see cref="TradeCharges"/>) and the ledger's costs of the account
/// as such (<see cref="LedgerCharges"/>, see <see cref="LedgerCosts"/>; never the costs of trades). <see cref="LedgerFrom"/> is the first date of the account's ledger (null: no ledger stored), the date its costs are known from.
/// </summary>
internal sealed record AccountIncome(IReadOnlyList<CashFlow> Dividends, IReadOnlyList<CashFlow> TradeCharges, IReadOnlyList<CashFlow> LedgerCharges, DateOnly? LedgerFrom)
{
    public static AccountIncome None { get; } = new([], [], [], null);
}
