using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.Income;
using static XDrishti.Application.Tests.Portfolio.HoldingFixtures;

namespace XDrishti.Application.Tests.Portfolio.Income;

/// <summary>Made-up dividends and costs for the income calculators (valuation date 2026-10-11, account <see cref="HoldingFixtures.Account"/>).</summary>
internal static class IncomeFixtures
{
    public static readonly Guid OtherAccount = Guid.Parse("00000000-0000-7000-8000-0000000000a2");

    public static DateOnly D(int year, int month, int day) => new(year, month, day);

    /// <summary>A holding with its own ISIN (the shared fixture gives every holding the same one).</summary>
    public static HoldingData With(this HoldingData holding, string? isin, Guid? account = null) =>
        holding with { Isin = isin, AccountId = account ?? holding.AccountId };

    public static IncomeDividend Dividend(HoldingData? holding, DateOnly date, decimal amount, Guid? account = null) =>
        new(account ?? holding?.AccountId ?? Account, holding?.Id, date, amount);

    public static IncomeTradeCharge Charge(HoldingData holding, DateOnly date, decimal amount) =>
        new(holding.AccountId, holding.Isin, holding.TradingSymbol, date, amount);

    /// <summary>A ledger entry of class charges that is a cost of the account as such (DP charges unless said otherwise); the amount is what was paid.</summary>
    public static IncomeLedgerCharge Ledger(DateOnly date, decimal amount, Guid? account = null, string narration = "DP charges for the quarter", string description = "DEBIT") =>
        new(account ?? Account, date, description, narration, amount);

    public static IncomeBook Book(
        IEnumerable<HoldingData> holdings,
        IEnumerable<IncomeDividend>? dividends = null,
        IEnumerable<IncomeTradeCharge>? tradeCharges = null,
        IEnumerable<IncomeLedgerCharge>? ledgerCharges = null,
        IEnumerable<IncomeLedgerStart>? ledgerStarts = null,
        IEnumerable<Guid>? extraAccounts = null)
    {
        var all = holdings.ToList();
        var accounts = all.Select(h => h.AccountId).Concat(extraAccounts ?? []).Distinct().Order().ToList();
        return IncomeBook.Build(new IncomeData(
            accounts,
            [.. all.Select(h => new IncomeHolding(h.Id, h.AccountId, h.Isin, h.TradingSymbol, h.IsOpen))],
            [.. dividends ?? []],
            [.. tradeCharges ?? []],
            [.. ledgerCharges ?? []],
            [.. ledgerStarts ?? []]));
    }
}
