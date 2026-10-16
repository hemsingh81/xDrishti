using Microsoft.EntityFrameworkCore;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Portfolio;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Portfolio;

/// <summary>The set-based reads behind the attention items (ADR 0027): each is one query for all the given accounts or holdings. Nothing is tracked.</summary>
internal sealed class AttentionQueries(XDrishtiDbContext db) : IAttentionQueries
{
    public async ValueTask<IReadOnlyDictionary<Guid, PendingDividends>> PendingDividendsAsync(IReadOnlyCollection<Guid> accountIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accountIds);

        // The ledger entries of class dividend without a decision that could be entered (the same rule as the suggestions list); there are few of them, so they are grouped here.
        var pending = await db.LedgerEntries.AsNoTracking()
            .Where(x => accountIds.Contains(x.AccountId) && x.EntryClass == LedgerClass.Dividend && x.Amount > 0m && x.Amount <= DividendRules.MaxAmount && !db.DividendSuggestions.Any(d => d.Id == x.Id))
            .Select(x => new { x.AccountId, x.Id, x.EntryDate, x.CreatedAt })
            .ToListAsync(cancellationToken);
        return pending.GroupBy(x => x.AccountId).ToDictionary(g => g.Key, g => new PendingDividends(g.Count(), g.OrderByDescending(x => x.EntryDate).ThenByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).First().Id));
    }

    public async ValueTask<IReadOnlyDictionary<Guid, UnmatchedCounts>> UnmatchedAsync(IReadOnlyCollection<Guid> accountIds, DateOnly tradedSince, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accountIds);
        var holdings = await db.Holdings.AsNoTracking()
            .Where(x => accountIds.Contains(x.AccountId) && x.Status == HoldingStatus.Open && x.InstrumentId == null)
            .GroupBy(x => x.AccountId)
            .Select(g => new { Account = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var securities = await db.Transactions.AsNoTracking()
            .Where(x => accountIds.Contains(x.AccountId) && x.VoidedAt == null && x.Source == TradeSource.Broker && x.InstrumentId == null && x.TradeDate >= tradedSince)
            .Select(x => new { x.AccountId, x.Isin, x.TradingSymbol })
            .Distinct()
            .ToListAsync(cancellationToken);
        var traded = securities.GroupBy(x => x.AccountId).ToDictionary(g => g.Key, g => g.Count());
        var held = holdings.ToDictionary(x => x.Account, x => x.Count);
        return held.Keys.Union(traded.Keys).ToDictionary(id => id, id => new UnmatchedCounts(held.GetValueOrDefault(id), traded.GetValueOrDefault(id)));
    }

    public async ValueTask<IReadOnlyDictionary<Guid, HoldingFlags>> HoldingFlagsAsync(IReadOnlyCollection<Guid> holdingIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(holdingIds);
        if (holdingIds.Count == 0)
        {
            return new Dictionary<Guid, HoldingFlags>();
        }

        // A lot whose date the owner chose: one typed in, or an imported one whose date is not the default (the rule of the automatic rebuild).
        var rows = await db.Holdings.AsNoTracking()
            .Where(x => holdingIds.Contains(x.Id))
            .Select(x => new { x.Id, Open = x.Status == HoldingStatus.Open, HandDated = x.Lots.Any(l => l.Source == LotSource.Manual || (l.Source == LotSource.Import && !l.UsesDefaultDate)) })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(x => x.Id, x => new HoldingFlags(x.Open, x.HandDated));
    }
}
