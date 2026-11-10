using Microsoft.EntityFrameworkCore;
using XDrishti.Application.Abstractions.Market.Baskets;
using XDrishti.Application.Market.Baskets;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Market.SystemBaskets;

internal sealed class BasketSourceStore(XDrishtiDbContext db) : IBasketSourceStore
{
    public async ValueTask<IReadOnlyList<BasketSourceInfo>> ListAsync(IReadOnlyCollection<Guid>? basketIds, CancellationToken cancellationToken)
    {
        var sources = db.BasketSources.AsNoTracking();
        if (basketIds is not null)
        {
            sources = sources.Where(s => basketIds.Contains(s.BasketId));
        }

        var rows = await (
            from s in sources
            join b in db.Baskets.AsNoTracking() on s.BasketId equals b.Id
            orderby b.Name
            select new
            {
                s,
                b.Name,
                Members = db.BasketMembers.Count(m => m.BasketId == b.Id && m.ValidTo == null),
            }).ToListAsync(cancellationToken);
        return [.. rows.Select(r => new BasketSourceInfo(
            r.s.BasketId, r.Name, r.s.FileName, r.s.SourceUrl, r.s.HostAlt, r.s.ExpectedMin, r.s.ExpectedMax, r.s.Enabled, r.s.State, r.s.LastOkAt, r.s.LastCheckedAt,
            r.s.Etag, r.s.LastModified, r.s.Sha256, r.s.NextAttemptAt, r.s.FailCount, r.Members))];
    }

    public async ValueTask<IReadOnlyList<MasterInstrument>> FindInstrumentsAsync(IReadOnlyCollection<string> isins, IReadOnlyCollection<string> symbols, CancellationToken cancellationToken)
    {
        var canonical = symbols.Select(s => "NSE:" + s).ToArray();
        var rows = await db.Instruments.AsNoTracking()
            .Where(i => i.Type == "share" && ((i.Isin != null && isins.Contains(i.Isin)) || canonical.Contains(i.CanonicalSymbol)))
            .Select(i => new { i.Id, i.CanonicalSymbol, i.Isin, i.RetiredAt })
            .ToListAsync(cancellationToken);
        return [.. rows.Select(r => new MasterInstrument(r.Id, r.CanonicalSymbol.StartsWith("NSE:", StringComparison.Ordinal) ? r.CanonicalSymbol[4..] : r.CanonicalSymbol, r.Isin, r.RetiredAt))];
    }

    public async ValueTask<IReadOnlyList<FetchOutcomeView>> RecentFetchesAsync(Guid basketId, int take, CancellationToken cancellationToken)
    {
        var rows = await db.BasketFetches.AsNoTracking().Where(f => f.BasketId == basketId).OrderByDescending(f => f.FetchedAt).ThenByDescending(f => f.Id).Take(take).ToListAsync(cancellationToken);
        return [.. rows.Select(f => new FetchOutcomeView(f.BasketId, f.FetchedAt, f.Outcome, f.HttpStatus, f.Rows, f.Added, f.Removed, f.Unmatched, f.Error, f.Forced))];
    }

    public async ValueTask RecordAsync(SourceUpdate source, FetchRecord fetch, CancellationToken cancellationToken)
    {
        Stage(source, fetch);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Adds the fetch row and sets the source's new state on the tracked context (saved by the caller, with whatever else belongs to the same transaction).</summary>
    internal void Stage(SourceUpdate source, FetchRecord fetch)
    {
        var record = db.BasketSources.Local.FirstOrDefault(s => s.BasketId == source.BasketId) ?? db.BasketSources.First(s => s.BasketId == source.BasketId);
        record.Enabled = source.Enabled;
        record.State = source.State;
        record.LastCheckedAt = source.LastCheckedAt;
        record.LastOkAt = source.LastOkAt;
        record.Etag = source.Etag;
        record.LastModified = source.LastModified;
        record.Sha256 = source.Sha256;
        record.NextAttemptAt = source.NextAttemptAt;
        record.FailCount = source.FailCount;
        db.BasketFetches.Add(new BasketFetchRecord
        {
            Id = fetch.Id,
            BasketId = fetch.BasketId,
            FetchedAt = fetch.FetchedAt,
            HttpStatus = fetch.HttpStatus,
            Etag = fetch.Etag,
            LastModified = fetch.LastModified,
            Sha256 = fetch.Sha256,
            Rows = fetch.Rows,
            Outcome = fetch.Outcome,
            Added = fetch.Added,
            Removed = fetch.Removed,
            Unmatched = fetch.Unmatched,
            Warnings = fetch.WarningsJson,
            Error = fetch.Error,
            Forced = fetch.Forced,
            Actor = fetch.Actor,
            SettingsSnapshot = fetch.SettingsSnapshot,
        });
    }
}
