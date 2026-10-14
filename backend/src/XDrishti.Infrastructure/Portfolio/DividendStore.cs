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
