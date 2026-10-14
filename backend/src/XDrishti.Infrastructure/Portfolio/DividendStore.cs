using Microsoft.EntityFrameworkCore;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Portfolio;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Portfolio;

/// <summary>The dividends (<c>portfolio.dividends</c>, ADR 0026). Reads are not tracked; new dividends are collected and saved by the portfolio unit of work. Nothing is deleted.</summary>
internal sealed class DividendStore(XDrishtiDbContext db) : IDividendStore
{
    public async ValueTask<Dividend?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Dividends.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public void Add(Dividend dividend)
    {
        ArgumentNullException.ThrowIfNull(dividend);
        db.Dividends.Add(dividend);
    }

    public void AddRange(IEnumerable<Dividend> dividends)
    {
        ArgumentNullException.ThrowIfNull(dividends);
        db.Dividends.AddRange(dividends);
    }

    public async ValueTask<IReadOnlySet<string>> StoredIdsAsync(Guid accountId, IReadOnlyCollection<string> externalIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(externalIds);
        if (externalIds.Count == 0)
        {
            return new HashSet<string>();
        }

        var ids = externalIds as string[] ?? [.. externalIds];
        return (await db.Dividends.AsNoTracking()
            .Where(x => x.AccountId == accountId && x.VoidedAt == null && x.ExternalId != null && ids.Contains(x.ExternalId))
            .Select(x => x.ExternalId!)
            .ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);
    }

    public async ValueTask<IReadOnlyList<StoredDividendKey>> ListManualActiveAsync(Guid accountId, DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        await db.Dividends.AsNoTracking()
            .Where(x => x.AccountId == accountId && x.VoidedAt == null && x.Source == DividendSource.Manual && x.PayDate >= from && x.PayDate <= to)
            .Select(x => new StoredDividendKey(x.HoldingId, x.PayDate, x.Amount, x.InstrumentId, x.Isin, x.TradingSymbol))
            .ToListAsync(cancellationToken);

    public async ValueTask<int> VoidBatchAsync(Guid batchId, string by, DateTimeOffset at, CancellationToken cancellationToken)
    {
        var actor = by.Length <= Dividend.MaxCreatedByLength ? by : by[..Dividend.MaxCreatedByLength];
        var time = at.ToUniversalTime();
        return await db.Dividends
            .Where(x => x.BatchId == batchId && x.VoidedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.VoidedAt, time).SetProperty(x => x.VoidedBy, actor), cancellationToken);
    }

    public async ValueTask<bool> ExistsSameAsync(Guid accountId, Guid? holdingId, DateOnly payDate, decimal amount, CancellationToken cancellationToken) =>
        await db.Dividends.AsNoTracking()
            .AnyAsync(x => x.AccountId == accountId && x.HoldingId == holdingId && x.PayDate == payDate && x.Amount == amount && x.VoidedAt == null, cancellationToken);

    public async ValueTask<(IReadOnlyList<Dividend> Items, int Total)> ListForHoldingAsync(Guid holdingId, int page, int pageSize, CancellationToken cancellationToken) =>
        await PageAsync(db.Dividends.AsNoTracking().Where(x => x.HoldingId == holdingId), page, pageSize, cancellationToken);

    public async ValueTask<(IReadOnlyList<Dividend> Items, int Total)> ListForAccountAsync(Guid accountId, int page, int pageSize, CancellationToken cancellationToken) =>
        await PageAsync(db.Dividends.AsNoTracking().Where(x => x.AccountId == accountId), page, pageSize, cancellationToken);

    private static async Task<(IReadOnlyList<Dividend>, int)> PageAsync(IQueryable<Dividend> query, int page, int pageSize, CancellationToken cancellationToken)
    {
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(x => x.PayDate).ThenByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);
        return (items, total);
    }
}
