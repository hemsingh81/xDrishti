using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.Returns;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.Income;

/// <summary>
/// The recorded dividends and costs of a scope, sorted out per holding and per account (ADR 0026). A dividend belongs to the holding it was entered for, or to its account when it has none. The charges of a trade belong to the holding of the same account
/// that the trade's security resolves to by the rule <c>HoldingMatcher</c> applies when trades are attached to holdings — by ISIN, else by symbol when either side has no ISIN (an ISIN never joins two different ISINs) — among the account's OPEN holdings; when
/// more than one open holding fits (an NSE and a BSE row of one ISIN) the trade is ambiguous and, as there, attached to none. Only when no open holding fits at all, the account's closed holdings are tried by the same rule (a holding sold out still has the
/// charges of its trades). A trade that fits no single holding belongs to the account. The ledger's entries count only when they are costs of the account as such (<see cref="LedgerCosts"/>). So every recorded rupee is in exactly one place and the
/// places add up to the total. Pure: no I/O.
/// </summary>
internal sealed class IncomeBook
{
    private readonly Dictionary<Guid, HoldingIncome> _holdings;

    private readonly Dictionary<Guid, AccountIncome> _accounts;

    private IncomeBook(Dictionary<Guid, HoldingIncome> holdings, Dictionary<Guid, AccountIncome> accounts, IReadOnlyList<Guid> accountIds)
    {
        _holdings = holdings;
        _accounts = accounts;
        AccountIds = accountIds;
    }

    /// <summary>A book with nothing in it.</summary>
    public static IncomeBook Empty { get; } = new([], [], []);

    /// <summary>The accounts of the scope the book was read for.</summary>
    public IReadOnlyList<Guid> AccountIds { get; }

    public static IncomeBook Build(IncomeData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var known = data.Holdings.Select(h => h.Id).ToHashSet();
        var indexes = data.Holdings.GroupBy(h => h.AccountId).ToDictionary(g => g.Key, g => new SecurityIndex(g));
        var holdingDividends = new Dictionary<Guid, List<CashFlow>>();
        var holdingCharges = new Dictionary<Guid, List<CashFlow>>();
        var accountDividends = new Dictionary<Guid, List<CashFlow>>();
        var accountTradeCharges = new Dictionary<Guid, List<CashFlow>>();
        var accountLedgerCharges = new Dictionary<Guid, List<CashFlow>>();

        foreach (var dividend in data.Dividends)
        {
            var flow = new CashFlow(dividend.PayDate, dividend.Amount);
            if (dividend.HoldingId is { } holdingId && known.Contains(holdingId))
            {
                Add(holdingDividends, holdingId, flow);
            }
            else
            {
                Add(accountDividends, dividend.AccountId, flow);
            }
        }

        foreach (var charge in data.TradeCharges)
        {
            var flow = new CashFlow(charge.TradeDate, charge.Amount);
            if (indexes.TryGetValue(charge.AccountId, out var index) && index.Find(charge.Isin, charge.TradingSymbol) is { } holdingId)
            {
                Add(holdingCharges, holdingId, flow);
            }
            else
            {
                Add(accountTradeCharges, charge.AccountId, flow);
            }
        }

        foreach (var charge in data.LedgerCharges.Where(c => LedgerCosts.IsAccountLevel(c.Description, c.Narration)))
        {
            Add(accountLedgerCharges, charge.AccountId, new CashFlow(charge.Date, charge.Amount));
        }

        var holdings = new Dictionary<Guid, HoldingIncome>();
        foreach (var id in holdingDividends.Keys.Concat(holdingCharges.Keys).Distinct())
        {
            holdings[id] = new HoldingIncome(Sorted(holdingDividends, id), Sorted(holdingCharges, id));
        }

        var starts = data.LedgerStarts.ToDictionary(s => s.AccountId, s => s.From);
        var accounts = new Dictionary<Guid, AccountIncome>();
        foreach (var id in data.AccountIds)
        {
            accounts[id] = new AccountIncome(Sorted(accountDividends, id), Sorted(accountTradeCharges, id), SummedByDay(accountLedgerCharges, id), starts.TryGetValue(id, out var from) ? from : null);
        }

        return new IncomeBook(holdings, accounts, data.AccountIds);
    }

    /// <summary>What is recorded for a holding (nothing when none).</summary>
    public HoldingIncome Of(Guid holdingId) => _holdings.GetValueOrDefault(holdingId) ?? HoldingIncome.None;

    /// <summary>What is recorded for an account and none of its holdings (nothing when none).</summary>
    public AccountIncome Account(Guid accountId) => _accounts.GetValueOrDefault(accountId) ?? AccountIncome.None;

    private static void Add(Dictionary<Guid, List<CashFlow>> lists, Guid key, CashFlow flow)
    {
        if (!lists.TryGetValue(key, out var list))
        {
            lists[key] = list = [];
        }

        list.Add(flow);
    }

    private static List<CashFlow> Sorted(Dictionary<Guid, List<CashFlow>> lists, Guid key) =>
        lists.TryGetValue(key, out var list) ? [.. list.OrderBy(f => f.Date).ThenBy(f => f.Amount)] : [];

    /// <summary>The flows of a key with those of one day summed (the day's refunds against its charges), a day that nets to nothing left out.</summary>
    private static List<CashFlow> SummedByDay(Dictionary<Guid, List<CashFlow>> lists, Guid key) =>
        lists.TryGetValue(key, out var list)
            ? [.. list.GroupBy(f => f.Date).Select(g => new CashFlow(g.Key, g.Sum(f => f.Amount))).Where(f => f.Amount != 0m).OrderBy(f => f.Date)]
            : [];

    /// <summary>Finds the holding of one account that a trade's security belongs to: among the open holdings, then among the closed ones; a security that fits more than one holding of a tier fits none.</summary>
    private sealed class SecurityIndex
    {
        private readonly Tier _open = new();

        private readonly Tier _closed = new();

        public SecurityIndex(IEnumerable<IncomeHolding> holdings)
        {
            foreach (var holding in holdings)
            {
                (holding.IsOpen ? _open : _closed).Add(holding);
            }
        }

        public Guid? Find(string? isin, string? symbol)
        {
            var open = _open.Find(isin, symbol);
            return open.Kind == MatchKind.None ? _closed.Find(isin, symbol).Id : open.Id;
        }

        private enum MatchKind
        {
            None,
            One,
            Ambiguous,
        }

        private readonly record struct Match(MatchKind Kind, Guid? Id);

        private sealed class Tier
        {
            private readonly Dictionary<string, List<IncomeHolding>> _byIsin = new(StringComparer.Ordinal);

            private readonly Dictionary<string, List<IncomeHolding>> _bySymbol = new(StringComparer.Ordinal);

            private readonly Dictionary<Guid, string?> _isins = [];

            public void Add(IncomeHolding holding)
            {
                var isin = TradeRules.NormaliseIsin(holding.Isin);
                _isins[holding.Id] = isin;
                if (isin is not null)
                {
                    Add(_byIsin, isin, holding);
                }

                var symbol = TradeRules.NormaliseSymbol(holding.TradingSymbol);
                if (symbol.Length > 0)
                {
                    Add(_bySymbol, symbol, holding);
                }
            }

            public Match Find(string? isin, string? symbol)
            {
                var code = TradeRules.NormaliseIsin(isin);
                if (code is not null && _byIsin.TryGetValue(code, out var byIsin))
                {
                    return One(byIsin);
                }

                var name = TradeRules.NormaliseSymbol(symbol);
                return name.Length > 0 && _bySymbol.TryGetValue(name, out var bySymbol)
                    ? One([.. bySymbol.Where(h => code is null || _isins[h.Id] is null)])
                    : new Match(MatchKind.None, null);
            }

            private static Match One(List<IncomeHolding> candidates) => candidates.Count switch
            {
                0 => new Match(MatchKind.None, null),
                1 => new Match(MatchKind.One, candidates[0].Id),
                _ => new Match(MatchKind.Ambiguous, null),
            };

            private static void Add(Dictionary<string, List<IncomeHolding>> index, string key, IncomeHolding holding)
            {
                if (!index.TryGetValue(key, out var list))
                {
                    index[key] = list = [];
                }

                list.Add(holding);
            }
        }
    }
}
