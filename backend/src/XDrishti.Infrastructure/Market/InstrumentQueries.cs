using Microsoft.EntityFrameworkCore;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Market.Search;
using XDrishti.Domain.Market;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Market;

internal sealed class InstrumentQueries(XDrishtiDbContext db) : IInstrumentQueries
{
    public async ValueTask<InstrumentPage> SearchAsync(InstrumentSearch search, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(search);
        var matches = Filter(db.Instruments.AsNoTracking(), search);
        var total = await matches.CountAsync(cancellationToken);
        var items = await Sort(matches, search)
            .Skip((search.Page - 1) * search.PageSize)
            .Take(search.PageSize)
            .Select(x => new
            {
                x.Id,
                x.CanonicalSymbol,
                x.Name,
                x.DisplayName,
                x.Type,
                x.Isin,
                x.Series,
                x.Sector,
                x.LotSize,
                x.TickSize,
                x.ExpiryDate,
                x.FnoEligible,
                AsmGsm = x.AsmGsmFlag != null,
                Retired = x.RetiredAt != null,
            })
            .ToListAsync(cancellationToken);
        var tracked = await TrackedAsync([.. items.Select(x => x.Id)], cancellationToken);
        var summaries = items.Select(x => new InstrumentSummary(
            x.Id, x.CanonicalSymbol, x.Name, x.DisplayName, x.Type, x.Isin, x.Series, x.Sector, x.LotSize, x.TickSize, x.ExpiryDate,
            x.FnoEligible, x.AsmGsm, x.Retired, Info(tracked, x.Id, x.Retired))).ToList();
        return new InstrumentPage(summaries, search.Page, search.PageSize, total);
    }

    public async ValueTask<InstrumentFacets> FacetsAsync(CancellationToken cancellationToken)
    {
        var active = db.Instruments.AsNoTracking().Where(x => x.RetiredAt == null);
        var total = await active.CountAsync(cancellationToken);
        var retired = await db.Instruments.AsNoTracking().CountAsync(x => x.RetiredAt != null, cancellationToken);
        var fno = await active.CountAsync(x => x.FnoEligible, cancellationToken);
        var asmGsm = await active.CountAsync(x => x.AsmGsmFlag != null, cancellationToken);
        var tracked = await db.TrackedInstruments.AsNoTracking().CountAsync(t => t.Active, cancellationToken);
        var trackedRetired = await db.Instruments.AsNoTracking()
            .CountAsync(x => x.RetiredAt != null && db.TrackedInstruments.Any(t => t.Id == x.Id && t.Active), cancellationToken);

        var types = await active.GroupBy(x => x.Type).Select(g => new FacetCount(g.Key, g.Count())).ToListAsync(cancellationToken);
        // Only shares get sectors, so the sector facet counts shares (and "Unclassified" = shares without a sector), matching the sector filter.
        var sectors = await active.Where(x => x.Type == InstrumentTypeCodes.Share).GroupBy(x => x.Sector).Select(g => new { Value = g.Key, Count = g.Count() }).ToListAsync(cancellationToken);
        var series = await active.Where(x => x.Series != null).GroupBy(x => x.Series!).Select(g => new FacetCount(g.Key, g.Count())).ToListAsync(cancellationToken);

        var named = sectors.Where(s => s.Value != null).OrderBy(s => s.Value, StringComparer.Ordinal).Select(s => new FacetCount(s.Value!, s.Count));
        var unclassified = sectors.Where(s => s.Value == null).Select(s => new FacetCount(InstrumentSearch.Unclassified, s.Count));
        return new InstrumentFacets(
            total,
            retired,
            [.. types.OrderBy(t => TypeOrder(t.Value))],
            [.. named, .. unclassified],
            [.. series.OrderByDescending(s => s.Count).ThenBy(s => s.Value, StringComparer.Ordinal)],
            fno,
            asmGsm,
            tracked,
            trackedRetired);
    }

    public async ValueTask<InstrumentDetail?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var x = await db.Instruments.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (x is null)
        {
            return null;
        }

        var ids = await db.InstrumentBrokerIds.AsNoTracking()
            .Where(b => b.InstrumentId == id)
            .OrderBy(b => b.Broker)
            .Select(b => new BrokerIdView(b.Broker, b.BrokerId, b.Segment))
            .ToListAsync(cancellationToken);
        return new InstrumentDetail(
            x.Id, x.CanonicalSymbol, x.Exchange, x.ExchangeSegment, x.Type, x.Name, x.DisplayName, x.Isin, x.Series, x.LotSize, x.TickSize, x.ExpiryDate,
            x.UnderlyingSymbol, x.Sector, x.SectorSource, x.FnoEligible, x.AsmGsmFlag != null, x.AsmGsmFlag, x.AsmGsmCategory, x.MtfLeverage,
            x.FreezeQuantity, x.FirstSeenAt, x.LastSeenAt, x.RetiredAt, ids, Info(await TrackedAsync([id], cancellationToken), id, x.RetiredAt != null));
    }

    public async ValueTask<IReadOnlyList<ResolveCandidate>> FindForResolveAsync(
        IReadOnlyCollection<string> symbols, IReadOnlyCollection<string> isins, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(symbols);
        ArgumentNullException.ThrowIfNull(isins);
        return await db.Instruments.AsNoTracking()
            .Where(x => symbols.Contains(x.CanonicalSymbol.ToUpper())
                || (x.Isin != null && x.Type != InstrumentTypeCodes.Future && isins.Contains(x.Isin.ToUpper())))
            .Select(x => new ResolveCandidate(
                x.Id, x.CanonicalSymbol, x.Name, x.Type, x.Isin, x.RetiredAt != null, db.TrackedInstruments.Any(t => t.Id == x.Id && t.Active)))
            .ToListAsync(cancellationToken);
    }

    private async Task<Dictionary<Guid, TrackedInstrument>> TrackedAsync(Guid[] ids, CancellationToken cancellationToken) =>
        ids.Length == 0
            ? []
            : await db.TrackedInstruments.AsNoTracking().Where(t => ids.Contains(t.Id)).ToDictionaryAsync(t => t.Id, cancellationToken);

    private static TrackedInfo Info(Dictionary<Guid, TrackedInstrument> tracked, Guid id, bool retired) =>
        TrackedInfoMapper.From(tracked.GetValueOrDefault(id), retired);

    private static int TypeOrder(string type) => InstrumentTypeCodes.TryParse(type, out var parsed) ? (int)parsed : int.MaxValue;

    private IQueryable<InstrumentRecord> Filter(IQueryable<InstrumentRecord> query, InstrumentSearch s)
    {
        // Tracked instruments that retired stay in the tracked list (with a warning), whatever includeRetired says.
        if (s.Tracked == true)
        {
            query = query.Where(x => db.TrackedInstruments.Any(t => t.Id == x.Id && t.Active));
        }
        else
        {
            if (!s.IncludeRetired)
            {
                query = query.Where(x => x.RetiredAt == null);
            }

            if (s.Tracked == false)
            {
                query = query.Where(x => !db.TrackedInstruments.Any(t => t.Id == x.Id && t.Active));
            }
        }

        if (s.Text is { } text)
        {
            var pattern = "%" + LikePattern.Escape(text) + "%";
            query = query.Where(x =>
                EF.Functions.ILike(x.CanonicalSymbol, pattern, LikePattern.EscapeCharacter) || EF.Functions.ILike(x.Name, pattern, LikePattern.EscapeCharacter)
                || EF.Functions.ILike(x.DisplayName, pattern, LikePattern.EscapeCharacter) || (x.Isin != null && EF.Functions.ILike(x.Isin, pattern, LikePattern.EscapeCharacter)));
        }

        if (s.Type is { } type)
        {
            var code = type.ToCode();
            query = query.Where(x => x.Type == code);
        }

        if (s.Sector is { } sector)
        {
            query = sector == InstrumentSearch.Unclassified
                ? query.Where(x => x.Type == InstrumentTypeCodes.Share && x.Sector == null)
                : query.Where(x => x.Sector == sector);
        }

        if (s.Fno is { } fno)
        {
            query = query.Where(x => x.FnoEligible == fno);
        }

        if (s.AsmGsm is { } asm)
        {
            query = asm ? query.Where(x => x.AsmGsmFlag != null) : query.Where(x => x.AsmGsmFlag == null);
        }

        if (s.Series is { } series)
        {
            query = query.Where(x => x.Series == series);
        }

        return query;
    }

    // Always ends with the canonical symbol and the id, so pages never overlap or skip.
    private static IOrderedQueryable<InstrumentRecord> Sort(IQueryable<InstrumentRecord> query, InstrumentSearch s)
    {
        var ordered = (s.SortField, s.Descending) switch
        {
            (InstrumentSortField.Name, false) => query.OrderBy(x => x.Name),
            (InstrumentSortField.Name, true) => query.OrderByDescending(x => x.Name),
            (InstrumentSortField.Type, false) => query.OrderBy(x => x.Type),
            (InstrumentSortField.Type, true) => query.OrderByDescending(x => x.Type),
            (InstrumentSortField.Series, false) => query.OrderBy(x => x.Series),
            (InstrumentSortField.Series, true) => query.OrderByDescending(x => x.Series),
            (InstrumentSortField.Sector, false) => query.OrderBy(x => x.Sector),
            (InstrumentSortField.Sector, true) => query.OrderByDescending(x => x.Sector),
            (InstrumentSortField.Lot, false) => query.OrderBy(x => x.LotSize),
            (InstrumentSortField.Lot, true) => query.OrderByDescending(x => x.LotSize),
            (InstrumentSortField.Tick, false) => query.OrderBy(x => x.TickSize),
            (InstrumentSortField.Tick, true) => query.OrderByDescending(x => x.TickSize),
            (_, true) => query.OrderByDescending(x => x.CanonicalSymbol),
            _ => query.OrderBy(x => x.CanonicalSymbol),
        };
        return ordered.ThenBy(x => x.CanonicalSymbol).ThenBy(x => x.Id);
    }
}
