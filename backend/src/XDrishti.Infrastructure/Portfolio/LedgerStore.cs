using Microsoft.EntityFrameworkCore;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Portfolio;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Portfolio;

/// <summary>
/// The ledger entries (<c>portfolio.ledger_entries</c>, ADR 0025, 0029). Reads are not tracked and leave out voided entries; new entries are collected and saved by the portfolio unit of work (an insert of an entry whose identity is stored breaks the unique index); a batch
/// of imported entries is voided by one conditional statement that executes at once inside the current transaction. Nothing is deleted.
/// </summary>
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
            .Where(x => x.AccountId == accountId && x.VoidedAt == null && ids.Contains(x.ExternalId))
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
        var query = db.LedgerEntries.AsNoTracking().Where(x => x.AccountId == accountId && x.VoidedAt == null);
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

    public async ValueTask<DateOnly?> FirstDateAsync(Guid accountId, CancellationToken cancellationToken) =>
        await db.LedgerEntries.AsNoTracking().Where(x => x.AccountId == accountId && x.Source == LedgerSource.Broker).MinAsync(x => (DateOnly?)x.EntryDate, cancellationToken);

    public async ValueTask<IReadOnlyList<LedgerChargeOnDay>> ListBrokerChargesAsync(Guid accountId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var rows = await db.LedgerEntries.AsNoTracking()
            .Where(x => x.AccountId == accountId && x.Source == LedgerSource.Broker && x.EntryClass == LedgerClass.Charges && x.Amount < 0m && x.EntryDate >= from && x.EntryDate <= to)
            .OrderBy(x => x.EntryDate).ThenBy(x => x.Id)
            .Select(x => new { x.EntryDate, x.Amount, x.Description, x.Narration })
            .ToListAsync(cancellationToken);
        return [.. rows.Select(r => new LedgerChargeOnDay(r.EntryDate, -r.Amount, DepositoryCharges.Is(r.Description, r.Narration)))];
    }

    public async ValueTask<int> VoidBatchAsync(Guid batchId, string by, DateTimeOffset at, CancellationToken cancellationToken)
    {
        var actor = by.Length <= Transaction.MaxCreatedByLength ? by : by[..Transaction.MaxCreatedByLength];
        var time = at.ToUniversalTime();
        return await db.LedgerEntries
            .Where(x => x.BatchId == batchId && x.VoidedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.VoidedAt, time).SetProperty(x => x.VoidedBy, actor), cancellationToken);
    }

    public async ValueTask<int> SupersedeImportedChargesAsync(Guid accountId, IReadOnlyCollection<LedgerChargeOnDay> brokerCharges, DateTimeOffset at, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(brokerCharges);
        if (brokerCharges.Count == 0)
        {
            return 0;
        }

        var from = brokerCharges.Min(c => c.Date);
        var to = brokerCharges.Max(c => c.Date);
        var candidates = await db.LedgerEntries.AsNoTracking()
            .Where(x => x.AccountId == accountId && x.Source == LedgerSource.Import && x.VoidedAt == null && x.EntryClass == LedgerClass.Charges && x.Amount < 0m && x.EntryDate >= from && x.EntryDate <= to)
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id)
            .Select(x => new { x.Id, x.EntryDate, x.Amount })
            .ToListAsync(cancellationToken);
        if (candidates.Count == 0)
        {
            return 0;
        }

        var ids = LedgerSupersession.Covered(candidates.Select(c => (c.Id, c.EntryDate, -c.Amount)), brokerCharges);
        if (ids.Count == 0)
        {
            return 0;
        }

        var time = at.ToUniversalTime();
        var array = ids.ToArray();
        return await db.LedgerEntries
            .Where(x => array.Contains(x.Id) && x.VoidedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.VoidedAt, time).SetProperty(x => x.VoidedBy, LedgerEntry.SupersededReason), cancellationToken);
    }

    public async ValueTask<IReadOnlyList<LedgerTotal>> TotalsAsync(Guid accountId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        var query = db.LedgerEntries.AsNoTracking().Where(x => x.AccountId == accountId && x.VoidedAt == null);
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
