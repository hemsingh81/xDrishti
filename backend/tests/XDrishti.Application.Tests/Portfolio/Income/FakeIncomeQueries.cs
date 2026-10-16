using XDrishti.Application.Abstractions.Portfolio;

namespace XDrishti.Application.Tests.Portfolio.Income;

/// <summary>
/// The recorded dividends and costs of the accounts, served as they are. The holdings (their keys) and so the accounts come from the function given; <see cref="ExtraAccounts"/> are accounts without holdings. The scope filter is the account only
/// (every account counts in totals). Counts the reads.
/// </summary>
internal sealed class FakeIncomeQueries(Func<IEnumerable<IncomeHolding>>? holdings = null) : IIncomeQueries
{
    public List<Guid> ExtraAccounts { get; } = [];

    public List<IncomeDividend> Dividends { get; } = [];

    public List<IncomeTradeCharge> TradeCharges { get; } = [];

    public List<IncomeLedgerCharge> LedgerCharges { get; } = [];

    public List<IncomeLedgerStart> LedgerStarts { get; } = [];

    public int Reads { get; private set; }

    public IncomeScope? LastScope { get; private set; }

    /// <summary>A fake whose holdings are the given ones (read at every call, so the list may change).</summary>
    public static FakeIncomeQueries For(IEnumerable<HoldingData> holdings) =>
        new(() => holdings.Select(h => new IncomeHolding(h.Id, h.AccountId, h.Isin, h.TradingSymbol, h.IsOpen)).ToList());

    public ValueTask<IncomeData> ReadAsync(IncomeScope scope, CancellationToken cancellationToken)
    {
        Reads++;
        LastScope = scope;
        var all = (holdings?.Invoke() ?? []).ToList();
        var accounts = all.Select(h => h.AccountId).Concat(ExtraAccounts).Distinct().Order().ToList();
        if (scope.AccountId is { } only)
        {
            accounts = [.. accounts.Where(a => a == only)];
        }

        return ValueTask.FromResult(new IncomeData(
            accounts,
            [.. all.Where(h => accounts.Contains(h.AccountId))],
            [.. Dividends.Where(d => accounts.Contains(d.AccountId))],
            [.. TradeCharges.Where(c => accounts.Contains(c.AccountId))],
            [.. LedgerCharges.Where(c => accounts.Contains(c.AccountId))],
            [.. LedgerStarts.Where(s => accounts.Contains(s.AccountId))]));
    }
}
