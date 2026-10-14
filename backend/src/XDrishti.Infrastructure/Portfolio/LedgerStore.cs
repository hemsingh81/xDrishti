using Microsoft.EntityFrameworkCore;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Portfolio;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Portfolio;

/// <summary>The ledger entries (<c>portfolio.ledger_entries</c>, ADR 0025). Reads are not tracked; new entries are collected and saved by the portfolio unit of work (an insert of an entry whose identity is stored breaks the unique index). Nothing is deleted.</summary>
internal sealed class LedgerStore(XDrishtiDbContext db) : ILedgerStore
{
    public async ValueTask<IReadOnlySet<string>> StoredIdsAsync(Guid accountId, IReadOnlyCollection<string> externalIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(externalIds);
        if (externalIds.Count == 0)
        {
            return new HashSet<string>();
        }

        var ids = externalIds as string[] ?? [.. externalIds];
        return (await db.LedgerEntries.AsNoTracking()
            .Where(x => x.AccountId == accountId && ids.Contains(x.ExternalId))
            .Select(x => x.ExternalId)
            .ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);
    }

    public void Add(IEnumerable<LedgerEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        db.LedgerEntries.AddRange(entries);
    }

    public async ValueTask<(IReadOnlyList<LedgerEntry> Items, int Total)> ListAsync(
        Guid accountId, DateOnly? from, DateOnly? to, LedgerClass? entryClass, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = db.LedgerEntries.AsNoTracking().Where(x => x.AccountId == accountId);
        if (from is { } start)
        {
            query = query.Where(x => x.EntryDate >= start);
        }

        if (to is { } end)
        {
            query = query.Where(x => x.EntryDate <= end);
        }

        if (entryClass is { } wanted)
        {
            query = query.Where(x => x.EntryClass == wanted);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(x => x.EntryDate).ThenByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public async ValueTask<IReadOnlyList<LedgerTotal>> TotalsAsync(Guid accountId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        var query = db.LedgerEntries.AsNoTracking().Where(x => x.AccountId == accountId);
        if (from is { } start)
        {
            query = query.Where(x => x.EntryDate >= start);
        }

        if (to is { } end)
        {
            query = query.Where(x => x.EntryDate <= end);
        }

        var rows = await query.GroupBy(x => x.EntryClass).Select(g => new { Class = g.Key, Count = g.Count(), Amount = g.Sum(x => x.Amount) }).ToListAsync(cancellationToken);
        return [.. rows.Select(r => new LedgerTotal(r.Class, r.Count, r.Amount))];
    }
}
