using Microsoft.EntityFrameworkCore;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Portfolio;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Portfolio;

/// <summary>
/// The trades (<c>portfolio.transactions</c>, ADR 0023). Reads are not tracked; new trades are collected and saved by the portfolio unit of work (an insert of a trade whose identity is stored and not voided breaks the partial unique index);
/// a batch is voided by one conditional statement that executes at once inside the current transaction. Nothing is deleted.
/// </summary>
internal sealed class TradeStore(XDrishtiDbContext db) : ITradeStore
{
    public async ValueTask<IReadOnlyList<Transaction>> ListActiveAsync(Guid accountId, CancellationToken cancellationToken) =>
        await db.Transactions.AsNoTracking()
            .Where(x => x.AccountId == accountId && x.VoidedAt == null)
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Seq).ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

    public async ValueTask<IReadOnlyList<Transaction>> ListActiveForSecurityAsync(Guid accountId, string? isin, string symbol, CancellationToken cancellationToken) =>
        await db.Transactions.AsNoTracking()
            .Where(x => x.AccountId == accountId && x.VoidedAt == null
                && ((isin != null && x.Isin == isin) || (symbol != string.Empty && x.TradingSymbol == symbol && (isin == null || x.Isin == null))))
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Seq).ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

    public async ValueTask<IReadOnlyList<TradeSecurity>> ListBrokerSecuritiesAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var pairs = await db.Transactions.AsNoTracking()
            .Where(x => x.AccountId == accountId && x.VoidedAt == null && x.Source == TradeSource.Broker)
            .Select(x => new { x.Isin, x.TradingSymbol })
            .Distinct()
            .OrderBy(x => x.Isin).ThenBy(x => x.TradingSymbol)
            .ToListAsync(cancellationToken);
        return [.. pairs.Select(x => new TradeSecurity(x.Isin, x.TradingSymbol))];
    }

    public async ValueTask<IReadOnlySet<string>> StoredIdsAsync(Guid accountId, IReadOnlyCollection<string> externalIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(externalIds);
        if (externalIds.Count == 0)
        {
            return new HashSet<string>();
        }

        var ids = externalIds as string[] ?? [.. externalIds];
        return (await db.Transactions.AsNoTracking()
            .Where(x => x.AccountId == accountId && x.VoidedAt == null && ids.Contains(x.ExternalId))
            .Select(x => x.ExternalId)
            .ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);
    }

    public async ValueTask<IReadOnlyList<Transaction>> ListBrokerAwaitingEnrichmentAsync(Guid accountId, IReadOnlyCollection<string> externalIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(externalIds);
        if (externalIds.Count == 0)
        {
            return [];
        }

        var ids = externalIds as string[] ?? [.. externalIds];
        return await db.Transactions
            .Where(x => x.AccountId == accountId && x.VoidedAt == null && x.Source == TradeSource.Broker && ids.Contains(x.ExternalId)
                && (x.Isin == null || x.TradingSymbol == string.Empty || x.Charges == 0m))
            .ToListAsync(cancellationToken);
    }

    public async ValueTask<IReadOnlyList<Transaction>> ListActiveOfBatchAsync(Guid batchId, CancellationToken cancellationToken) =>
        await db.Transactions.AsNoTracking()
            .Where(x => x.BatchId == batchId && x.VoidedAt == null)
            .OrderBy(x => x.Seq).ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

    public async ValueTask<Transaction?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Transactions.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async ValueTask<(IReadOnlyList<Transaction> Items, int Total)> ListForSecurityAsync(
        Guid accountId, string? isin, string symbol, int page, int pageSize, CancellationToken cancellationToken)
    {
        // A trade belongs to the security by ISIN, or by symbol when either side has no ISIN (a symbol never joins two different ISINs).
        var query = db.Transactions.AsNoTracking()
            .Where(x => x.AccountId == accountId
                && ((isin != null && x.Isin == isin) || (symbol != string.Empty && x.TradingSymbol == symbol && (isin == null || x.Isin == null))));
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(x => x.TradeDate).ThenByDescending(x => x.TradeTime).ThenByDescending(x => x.CreatedAt).ThenByDescending(x => x.Seq).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public async ValueTask<IReadOnlyList<TradeGroup>> ListBrokerGroupsAsync(Guid accountId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var groups = await db.Transactions.AsNoTracking()
            .Where(x => x.AccountId == accountId && x.VoidedAt == null && x.Source == TradeSource.Broker && x.Isin != null && x.TradeDate >= from && x.TradeDate <= to)
            .GroupBy(x => new { x.TradeDate, x.Isin, x.Side })
            .Select(g => new { g.Key.TradeDate, g.Key.Isin, g.Key.Side, Qty = g.Sum(x => x.Qty) })
            .ToListAsync(cancellationToken);
        return [.. groups.OrderBy(g => g.TradeDate).ThenBy(g => g.Isin, StringComparer.Ordinal).ThenBy(g => g.Side).Select(g => new TradeGroup(g.TradeDate, g.Isin!, g.Side, g.Qty))];
    }

    public async ValueTask<SupersedeOutcome> SupersedeBillRowsAsync(Guid accountId, IReadOnlyCollection<TradeGroup> brokerGroups, Guid runId, DateTimeOffset at, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(brokerGroups);
        if (brokerGroups.Count == 0)
        {
            return SupersedeOutcome.None;
        }

        var from = brokerGroups.Min(g => g.Date);
        var to = brokerGroups.Max(g => g.Date);
        var isins = brokerGroups.Select(g => g.Isin).Distinct(StringComparer.Ordinal).ToArray();
        var candidates = await db.Transactions.AsNoTracking()
            .Where(x => x.AccountId == accountId && x.VoidedAt == null && x.Source == TradeSource.Bill && x.TradeDate >= from && x.TradeDate <= to && x.Isin != null && isins.Contains(x.Isin))
            .Select(x => new { x.Id, x.TradeDate, x.Isin, x.Side, x.Qty })
            .ToListAsync(cancellationToken);
        var wanted = brokerGroups.GroupBy(g => (g.Date, g.Isin, g.Side)).ToDictionary(g => g.Key, g => g.Sum(x => x.Qty));
        var hits = candidates.Where(c => wanted.ContainsKey((c.TradeDate, c.Isin!, c.Side))).ToList();
        if (hits.Count == 0)
        {
            return SupersedeOutcome.None;
        }

        var differing = hits.GroupBy(c => (c.TradeDate, c.Isin!, c.Side)).Count(g => g.Sum(c => c.Qty) != wanted[g.Key]);
        var ids = hits.Select(c => c.Id).ToArray();
        var time = at.ToUniversalTime();
        var voided = await db.Transactions
            .Where(x => ids.Contains(x.Id) && x.VoidedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.VoidedAt, time).SetProperty(x => x.VoidedBy, Transaction.SupersededReason).SetProperty(x => x.SupersededBy, runId), cancellationToken);
        return new SupersedeOutcome(voided, differing);
    }

    public void Add(IEnumerable<Transaction> trades)
    {
        ArgumentNullException.ThrowIfNull(trades);
        db.Transactions.AddRange(trades);
    }

    public async ValueTask<int> VoidBatchAsync(Guid batchId, string by, DateTimeOffset at, CancellationToken cancellationToken)
    {
        var actor = by.Length <= Transaction.MaxCreatedByLength ? by : by[..Transaction.MaxCreatedByLength];
        var time = at.ToUniversalTime();
        return await db.Transactions
            .Where(x => x.BatchId == batchId && x.VoidedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.VoidedAt, time).SetProperty(x => x.VoidedBy, actor), cancellationToken);
    }
}
