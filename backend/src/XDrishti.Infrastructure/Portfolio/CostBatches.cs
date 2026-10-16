using Microsoft.EntityFrameworkCore;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Portfolio;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Portfolio;

/// <summary>The cost import batches (<c>portfolio.cost_batches</c>, ADR 0029). Reads project each batch with the number of its ledger entries that still count, in one query.</summary>
internal sealed class CostBatches(XDrishtiDbContext db) : ICostBatches
{
    public async ValueTask<CostBatch?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        await db.CostBatches.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async ValueTask<CostBatchRow?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var rows = await ReadAsync(db.CostBatches.AsNoTracking().Where(x => x.Id == id), cancellationToken);
        return rows.Count == 0 ? null : rows[0];
    }

    public async ValueTask<(IReadOnlyList<CostBatchRow> Items, int Total)> ListAsync(Guid? accountId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = db.CostBatches.AsNoTracking().Where(x => accountId == null || x.AccountId == accountId);
        var total = await query.CountAsync(cancellationToken);
        var rows = await ReadAsync(query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize), cancellationToken);
        return (rows, total);
    }

    public void Add(CostBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        db.CostBatches.Add(batch);
    }

    private async ValueTask<IReadOnlyList<CostBatchRow>> ReadAsync(IQueryable<CostBatch> batches, CancellationToken cancellationToken)
    {
        var rows = await batches
            .Select(b => new { Batch = b, Active = db.LedgerEntries.Count(e => e.BatchId == b.Id && e.VoidedAt == null) })
            .ToListAsync(cancellationToken);
        return [.. rows.Select(r => new CostBatchRow(r.Batch, r.Active))];
    }
}
