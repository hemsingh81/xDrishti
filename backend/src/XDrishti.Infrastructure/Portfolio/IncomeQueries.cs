using Microsoft.EntityFrameworkCore;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Portfolio;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Portfolio;

/// <summary>
/// What the income figures are made of (ADR 0026), read for the accounts of a scope in at most six set-based queries — never one per holding: the accounts (chosen like the holdings'), the holdings' keys (not read when the caller has them), the dividends that are
/// not voided, the charges of the trades that are not voided summed per account, security and day, the ledger's entries of class charges summed per account, day and text, and the first date of each account's ledger (these last two not read when the caller wants
/// holdings only). Reads are not tracked.
/// </summary>
internal sealed class IncomeQueries(XDrishtiDbContext db) : IIncomeQueries
{
    public async ValueTask<IncomeData> ReadAsync(IncomeScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var accounts = db.BrokerAccounts.AsNoTracking().Where(a => a.ArchivedAt == null);
        accounts = scope.AccountId is { } accountId
            ? accounts.Where(a => a.Id == accountId)
            : accounts.Where(a => a.Portfolio.Enabled && (!scope.TotalsOnly || a.Portfolio.IncludeInTotals));
        var ids = await accounts.Select(a => a.Id).OrderBy(id => id).ToListAsync(cancellationToken);
        if (ids.Count == 0)
        {
            return IncomeData.None;
        }

        IReadOnlyList<IncomeHolding> holdings = scope.Holdings ?? await db.Holdings.AsNoTracking()
            .Where(h => ids.Contains(h.AccountId))
            .Select(h => new IncomeHolding(h.Id, h.AccountId, h.Isin, h.TradingSymbol, h.Status == HoldingStatus.Open))
            .ToListAsync(cancellationToken);
        var dividends = await db.Dividends.AsNoTracking()
            .Where(d => ids.Contains(d.AccountId) && d.VoidedAt == null)
            .Select(d => new IncomeDividend(d.AccountId, d.HoldingId, d.PayDate, d.Amount))
            .ToListAsync(cancellationToken);
        var tradeCharges = await db.Transactions.AsNoTracking()
            .Where(t => ids.Contains(t.AccountId) && t.VoidedAt == null && t.Charges > 0m)
            .GroupBy(t => new { t.AccountId, t.Isin, t.TradingSymbol, t.TradeDate })
            .Select(g => new IncomeTradeCharge(g.Key.AccountId, g.Key.Isin, g.Key.TradingSymbol, g.Key.TradeDate, g.Sum(t => t.Charges)))
            .ToListAsync(cancellationToken);
        if (!scope.AccountLevel)
        {
            return new IncomeData(ids, holdings, dividends, tradeCharges, [], []);
        }

        var ledgerCharges = await db.LedgerEntries.AsNoTracking()
            .Where(e => ids.Contains(e.AccountId) && e.VoidedAt == null && e.EntryClass == LedgerClass.Charges)
            .GroupBy(e => new { e.AccountId, e.EntryDate, e.Description, e.Narration })
            .Select(g => new IncomeLedgerCharge(g.Key.AccountId, g.Key.EntryDate, g.Key.Description, g.Key.Narration, -g.Sum(e => e.Amount)))
            .ToListAsync(cancellationToken);
        var starts = await db.LedgerEntries.AsNoTracking()
            .Where(e => ids.Contains(e.AccountId) && e.Source == LedgerSource.Broker)
            .GroupBy(e => e.AccountId)
            .Select(g => new IncomeLedgerStart(g.Key, g.Min(e => e.EntryDate)))
            .ToListAsync(cancellationToken);
        return new IncomeData(ids, holdings, dividends, tradeCharges, [.. ledgerCharges.Where(c => c.Amount != 0m)], starts);
    }
}
