using Microsoft.EntityFrameworkCore;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Market.Baskets;
using XDrishti.Application.Market.Search;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Baskets;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Market;

internal sealed class BasketQueries(XDrishtiDbContext db) : IBasketQueries
{
    public async ValueTask<IReadOnlyList<BasketView>> ListAsync(bool includeArchived, CancellationToken cancellationToken)
    {
        var baskets = db.Baskets.AsNoTracking();
        if (!includeArchived)
        {
            baskets = baskets.Where(b => b.ArchivedAt == null);
        }

        var rows = await Project(baskets.OrderBy(b => b.ArchivedAt != null).ThenBy(b => b.Position).ThenBy(b => b.CreatedAt).ThenBy(b => b.Id)).ToListAsync(cancellationToken);
        return [.. rows.Select(ToView)];
    }

    public async ValueTask<BasketView?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        await Project(db.Baskets.AsNoTracking().Where(b => b.Id == id)).FirstOrDefaultAsync(cancellationToken) is { } row ? ToView(row) : null;

    public async ValueTask<BasketMembers?> MembersAsync(Guid basketId, DateTimeOffset? cutoff, DateOnly? asOf, CancellationToken cancellationToken)
    {
        var basket = await db.Baskets.AsNoTracking().Where(b => b.Id == basketId).Select(b => new { b.Name, b.ArchivedAt }).FirstOrDefaultAsync(cancellationToken);
        if (basket is null)
        {
            return null;
        }

        var members = db.BasketMembers.AsNoTracking().Where(m => m.BasketId == basketId);
        members = cutoff is { } at ? members.Where(m => m.ValidFrom < at && (m.ValidTo == null || m.ValidTo >= at)) : members.Where(m => m.ValidTo == null);

        var rows = await (
            from m in members
            join i in db.Instruments.AsNoTracking() on m.InstrumentId equals i.Id
            orderby m.Position, i.CanonicalSymbol, m.Id
            select new
            {
                m.Id,
                m.Position,
                m.ValidFrom,
                m.ValidTo,
                m.Note,
                m.AddedBy,
                i.CanonicalSymbol,
                InstrumentId = i.Id,
                i.Name,
                i.DisplayName,
                i.Type,
                i.Isin,
                i.Series,
                i.Sector,
                i.LotSize,
                i.TickSize,
                i.ExpiryDate,
                i.FnoEligible,
                AsmGsm = i.AsmGsmFlag != null,
                Retired = i.RetiredAt != null,
            }).ToListAsync(cancellationToken);

        var ids = rows.Select(r => r.InstrumentId).Distinct().ToArray();
        var tracked = ids.Length == 0
            ? []
            : await db.TrackedInstruments.AsNoTracking().Where(t => ids.Contains(t.Id)).ToDictionaryAsync(t => t.Id, cancellationToken);
        var items = rows.Select(r => new BasketMemberView(
            r.Id,
            new InstrumentSummary(
                r.InstrumentId, r.CanonicalSymbol, r.Name, r.DisplayName, r.Type, r.Isin, r.Series, r.Sector, r.LotSize, r.TickSize, r.ExpiryDate,
                r.FnoEligible, r.AsmGsm, r.Retired, TrackedInfoMapper.From(tracked.GetValueOrDefault(r.InstrumentId), r.Retired)),
            r.Position,
            r.ValidFrom,
            r.ValidTo,
            r.Note,
            r.AddedBy,
            null,
            null)).ToList();
        return new BasketMembers(basketId, basket.Name, basket.ArchivedAt, asOf, items.Count, items);
    }

    public async ValueTask<BasketHistory?> HistoryAsync(Guid basketId, int take, CancellationToken cancellationToken)
    {
        var name = await db.Baskets.AsNoTracking().Where(b => b.Id == basketId).Select(b => b.Name).FirstOrDefaultAsync(cancellationToken);
        if (name is null)
        {
            return null;
        }

        // A stay yields at most two entries (added, removed) and its newest one is ValidTo (or ValidFrom while it lasts), so the newest `take` stays hold the newest `take` entries.
        var rows = await (
            from m in db.BasketMembers.AsNoTracking().Where(m => m.BasketId == basketId)
            join i in db.Instruments.AsNoTracking() on m.InstrumentId equals i.Id
            orderby (m.ValidTo ?? m.ValidFrom) descending, m.Id descending
            select new { m.Id, m.ValidFrom, m.ValidTo, m.Note, m.AddedBy, m.RemovedBy, InstrumentId = i.Id, i.CanonicalSymbol, i.Name, i.Type })
            .Take(take)
            .ToListAsync(cancellationToken);

        var entries = new List<BasketHistoryEntry>(rows.Count * 2);
        foreach (var r in rows)
        {
            entries.Add(new BasketHistoryEntry(r.Id, "added", r.ValidFrom, r.InstrumentId, r.CanonicalSymbol, r.Name, r.Type, r.Note, r.AddedBy));
            if (r.ValidTo is { } removedAt)
            {
                entries.Add(new BasketHistoryEntry(r.Id, "removed", removedAt, r.InstrumentId, r.CanonicalSymbol, r.Name, r.Type, null, r.RemovedBy));
            }
        }

        // Newest first; for the same instant a removal comes before the addition (a move within a second still reads in order).
        var ordered = entries.OrderByDescending(e => e.At).ThenByDescending(e => e.Kind, StringComparer.Ordinal).ThenBy(e => e.Symbol, StringComparer.Ordinal).Take(take).ToList();
        return new BasketHistory(basketId, name, ordered);
    }

    public async ValueTask<IReadOnlyList<InstrumentBasketView>?> ForInstrumentAsync(Guid instrumentId, CancellationToken cancellationToken)
    {
        if (!await db.Instruments.AsNoTracking().AnyAsync(i => i.Id == instrumentId, cancellationToken))
        {
            return null;
        }

        var rows = await (
            from m in db.BasketMembers.AsNoTracking().Where(m => m.InstrumentId == instrumentId && m.ValidTo == null)
            join b in db.Baskets.AsNoTracking().Where(b => b.ArchivedAt == null) on m.BasketId equals b.Id
            orderby b.Position, b.CreatedAt, b.Id
            select new { b.Id, b.Name, b.Colour, b.Purpose, b.Position, m.ValidFrom }).ToListAsync(cancellationToken);
        return [.. rows.Select(r => new InstrumentBasketView(r.Id, r.Name, r.Colour, r.Purpose.ToCode(), r.Position, r.ValidFrom))];
    }

    private IQueryable<BasketRow> Project(IQueryable<Basket> baskets) =>
        baskets.Select(b => new BasketRow(
            b.Id,
            b.Name,
            b.Colour,
            b.Purpose,
            b.Description,
            b.Position,
            db.BasketMembers.Count(m => m.BasketId == b.Id && m.ValidTo == null),
            b.CreatedAt,
            b.ArchivedAt));

    private static BasketView ToView(BasketRow r) =>
        new(r.Id, r.Name, r.Colour, r.Purpose.ToCode(), r.Description, r.Position, r.MemberCount, r.CreatedAt, r.ArchivedAt, BasketKinds.Manual, false, null, null);

    private sealed record BasketRow(
        Guid Id, string Name, string Colour, BasketPurpose Purpose, string? Description, int Position, int MemberCount, DateTimeOffset CreatedAt, DateTimeOffset? ArchivedAt);
}
