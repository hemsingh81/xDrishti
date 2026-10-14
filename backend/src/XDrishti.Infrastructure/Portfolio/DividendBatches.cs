using Microsoft.EntityFrameworkCore;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Portfolio;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Portfolio;

/// <summary>The dividend import batches (<c>portfolio.dividend_batches</c>, ADR 0028). Reads project each batch with the number of its dividends that still count, in one query.</summary>
internal sealed class DividendBatches(XDrishtiDbContext db) : IDividendBatches
{
    public async ValueTask<DividendBatch?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        await db.DividendBatches.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async ValueTask<DividendBatchRow?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var rows = await ReadAsync(db.DividendBatches.AsNoTracking().Where(x => x.Id == id), cancellationToken);
        return rows.Count == 0 ? null : rows[0];
    }

    public async ValueTask<(IReadOnlyList<DividendBatchRow> Items, int Total)> ListAsync(Guid? accountId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = db.DividendBatches.AsNoTracking().Where(x => accountId == null || x.AccountId == accountId);
        var total = await query.CountAsync(cancellationToken);
        var rows = await ReadAsync(query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize), cancellationToken);
        return (rows, total);
    }

    public void Add(DividendBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        db.DividendBatches.Add(batch);
    }

    private async ValueTask<IReadOnlyList<DividendBatchRow>> ReadAsync(IQueryable<DividendBatch> batches, CancellationToken cancellationToken)
    {
        var rows = await batches
            .Select(b => new { Batch = b, Active = db.Dividends.Count(d => d.BatchId == b.Id && d.VoidedAt == null) })
            .ToListAsync(cancellationToken);
        return [.. rows.Select(r => new DividendBatchRow(r.Batch, r.Active))];
    }
}
