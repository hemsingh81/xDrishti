using Microsoft.EntityFrameworkCore;
using XDrishti.Application.Abstractions.Market.Actions;
using XDrishti.Application.Market.Actions;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Actions;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Market.Actions;

/// <summary>Reads of the corporate actions (no tracking). The tables are small (a handful of actions per instrument), the indexes serve the status and instrument filters.</summary>
internal sealed class CorporateActionQueries(XDrishtiDbContext db) : ICorporateActionQueries
{
    public async ValueTask<CorporateActionPage> ListAsync(CorporateActionSearch search, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(search);
        var rows =
            from a in db.CorporateActions.AsNoTracking()
            join i in db.Instruments.AsNoTracking() on a.InstrumentId equals i.Id
            where (search.Status == null || a.Status == search.Status) && (search.InstrumentId == null || a.InstrumentId == search.InstrumentId)
            select new { Action = a, Symbol = i.CanonicalSymbol, i.Name };

        var total = await rows.CountAsync(cancellationToken);
        var page = await rows
            .OrderByDescending(x => x.Action.ExDate).ThenBy(x => x.Symbol).ThenBy(x => x.Action.Kind)
            .Skip((search.Page - 1) * search.PageSize).Take(search.PageSize)
            .ToListAsync(cancellationToken);
        var pending = await GetPendingAsync(cancellationToken);
        return new CorporateActionPage([.. page.Select(x => CorporateActionView.From(x.Action, x.Symbol, x.Name))], search.Page, search.PageSize, total, pending.Count);
    }

    public async ValueTask<PendingActions> GetPendingAsync(CancellationToken cancellationToken)
    {
        var suggested = db.CorporateActions.AsNoTracking().Where(x => x.Status == CorporateActionStatus.Suggested);
        var count = await suggested.CountAsync(cancellationToken);
        var latest = count == 0 ? null : await suggested.MaxAsync(x => (DateTimeOffset?)x.CreatedAt, cancellationToken);
        return new PendingActions(count, latest);
    }

    public async ValueTask<IReadOnlyList<AppliedAction>> ListAppliedAsync(Guid instrumentId, CancellationToken cancellationToken) =>
        await db.CorporateActions.AsNoTracking()
            .Where(x => x.InstrumentId == instrumentId && x.Status == CorporateActionStatus.Applied)
            .OrderBy(x => x.ExDate)
            .Select(x => new AppliedAction(x.ExDate, x.Kind, x.Numerator, x.Denominator, x.Factor))
            .ToListAsync(cancellationToken);

    public async ValueTask<IReadOnlySet<DateOnly>> ListExDatesAsync(Guid instrumentId, CancellationToken cancellationToken)
    {
        var rows = await db.CorporateActions.AsNoTracking().Where(x => x.InstrumentId == instrumentId).Select(x => new { x.ExDate, x.DetectedExDate }).ToListAsync(cancellationToken);
        var dates = rows.Select(x => x.ExDate).ToHashSet();
        dates.UnionWith(rows.Where(x => x.DetectedExDate != null).Select(x => x.DetectedExDate!.Value));
        return dates;
    }

    public async ValueTask<IReadOnlyDictionary<Guid, DateOnly>> ListCsvAdjustedThroughAsync(IReadOnlyCollection<Guid> instrumentIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instrumentIds);
        var ids = instrumentIds.ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<Guid, DateOnly>();
        }

        var rows = await db.CsvFiles.AsNoTracking()
            .Where(x => x.UndoneAt == null && x.LastBar != null && x.InstrumentId != null && ids.Contains(x.InstrumentId.Value))
            .GroupBy(x => x.InstrumentId!.Value)
            .Select(g => new { InstrumentId = g.Key, Last = g.Max(x => x.LastBar) })
            .ToListAsync(cancellationToken);
        return rows.Where(x => x.Last != null).ToDictionary(x => x.InstrumentId, x => IndiaTime.Today(x.Last!.Value));
    }

    public async ValueTask<IReadOnlySet<ActionKey>> ListKeysAsync(IReadOnlyCollection<Guid> instrumentIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instrumentIds);
        var ids = instrumentIds.ToArray();
        var rows = await db.CorporateActions.AsNoTracking()
            .Where(x => ids.Contains(x.InstrumentId))
            .Select(x => new { x.InstrumentId, x.ExDate, x.Kind })
            .ToListAsync(cancellationToken);
        return rows.Select(x => new ActionKey(x.InstrumentId, x.ExDate, x.Kind)).ToHashSet();
    }

    public async ValueTask<ActionInstrument?> FindInstrumentAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Instruments.AsNoTracking().Where(x => x.Id == id).Select(x => new ActionInstrument(x.Id, x.CanonicalSymbol, x.Name)).FirstOrDefaultAsync(cancellationToken);
}
