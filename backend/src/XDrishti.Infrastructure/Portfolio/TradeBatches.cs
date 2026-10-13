using Microsoft.EntityFrameworkCore;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Portfolio;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Portfolio;

/// <summary>The import batches (<c>portfolio.trade_batches</c>, ADR 0023). Reads project each batch with the number of its trades that still count and the securities among them, in one query.</summary>
internal sealed class TradeBatches(XDrishtiDbContext db) : ITradeBatches
{
    public async ValueTask<TradeBatch?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        await db.TradeBatches.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async ValueTask<TradeBatchRow?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var rows = await ReadAsync(db.TradeBatches.AsNoTracking().Where(x => x.Id == id), cancellationToken);
        return rows.Count == 0 ? null : rows[0];
    }

    public async ValueTask<(IReadOnlyList<TradeBatchRow> Items, int Total)> ListAsync(Guid? accountId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = db.TradeBatches.AsNoTracking().Where(x => accountId == null || x.AccountId == accountId);
        var total = await query.CountAsync(cancellationToken);
        var rows = await ReadAsync(query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize), cancellationToken);
        return (rows, total);
    }

    public void Add(TradeBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        db.TradeBatches.Add(batch);
    }

    private async ValueTask<IReadOnlyList<TradeBatchRow>> ReadAsync(IQueryable<TradeBatch> batches, CancellationToken cancellationToken)
    {
        var rows = await batches
            .Select(b => new
            {
                Batch = b,
                Active = db.Transactions.Count(t => t.BatchId == b.Id && t.VoidedAt == null),
                Securities = db.Transactions.Where(t => t.BatchId == b.Id && t.VoidedAt == null).Select(t => t.Isin ?? t.TradingSymbol).Distinct().Count(),
            })
            .ToListAsync(cancellationToken);
        return [.. rows.Select(r => new TradeBatchRow(r.Batch, r.Active, r.Securities))];
    }
}
