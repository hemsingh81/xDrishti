using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Audit;
using XDrishti.Domain.Audit;

namespace XDrishti.Application.Audit;

/// <summary>One matched row with its place in the merged order.</summary>
internal sealed record AuditHit(AuditPosition Position, AuditEntryView View);

/// <summary>What one store gave in one request: its matches, whether it failed, and, when the scan cap stopped it, the last row it looked at.</summary>
internal sealed record AuditScan(AuditSource Source, IReadOnlyList<AuditHit> Hits, bool Failed, AuditPosition? Horizon, int Scanned);

/// <summary>One merged page: the rows, where the next page starts (null at the end) and what each store did.</summary>
internal sealed record AuditMerged(IReadOnlyList<AuditHit> Items, AuditPosition? Next, IReadOnlyList<AuditScan> Scans);

/// <summary>
/// The merged, keyset-paged read of the six stores (spec P8-22 decision 3). Each store is read with its own index from the cursor, newest first, and the matches of each are merged by <c>(time, source ordinal)</c> in memory, so a failing store
/// does not fail the page and the cost is bounded by sources times <c>limit + 1</c>. A filter that only the application can apply (actor kind, search over the redacted text) is applied in chunks of 500 rows up to <see cref="MaxScan"/> rows per store;
/// when the cap stops a store the page ends at the last row it looked at, so no row is skipped or repeated. Reads nothing but the stores.
/// </summary>
internal sealed class AuditReader(IEnumerable<IAuditSource> sources, ILogger<AuditReader> logger)
{
    public const int MaxScan = 5000;

    public const int ChunkSize = 500;

    private readonly Dictionary<AuditSource, IAuditSource> _sources = sources.ToDictionary(s => s.Source);

    public async ValueTask<AuditMerged> PageAsync(AuditCriteria criteria, AuditPosition? cursor, int limit, string? ownerEmail, CancellationToken cancellationToken)
    {
        var reads = criteria.Sources.Where(_sources.ContainsKey).Select(s => ScanAsync(_sources[s], criteria, cursor, limit, ownerEmail, cancellationToken)).ToList();
        var scans = await Task.WhenAll(reads);

        var merged = scans.SelectMany(s => s.Hits).OrderByDescending(h => h.Position.At).ThenByDescending(h => h.Position.Source).ToList();

        // A store that hit the cap may hold more matches further down than it showed: nothing older than the last row it looked at is certain.
        // With several capped stores the cut is the NEWEST of their horizons: a row between two horizons can still be preceded by an unseen match of the store with the newer one.
        AuditPosition? horizon = null;
        foreach (var scan in scans.Where(s => s.Horizon is not null))
        {
            if (horizon is null || scan.Horizon!.Value.IsNotAfter(horizon.Value))
            {
                horizon = scan.Horizon;
            }
        }

        var retained = horizon is { } h ? merged.Where(x => x.Position.IsNotAfter(h)).ToList() : merged;
        var page = retained.Take(limit).ToList();
        AuditPosition? next = null;
        if (retained.Count > limit)
        {
            next = page[^1].Position;
        }
        else if (horizon is { } stop)
        {
            next = stop; // everything up to the horizon is shown; the next page starts after it
        }

        return new AuditMerged(page, next, scans);
    }

    private async Task<AuditScan> ScanAsync(IAuditSource source, AuditCriteria criteria, AuditPosition? cursor, int limit, string? ownerEmail, CancellationToken cancellationToken)
    {
        var hits = new List<AuditHit>();
        var scanned = 0;
        try
        {
            var after = cursor is { } c ? AfterFor(source.Source, c) : null;
            var chunk = criteria.NeedsScan ? ChunkSize : limit + 1;
            criteria.Kinds.TryGetValue(source.Source, out var kinds);
            while (true)
            {
                var rows = await source.ReadAsync(new AuditRead(criteria.Window.Start, criteria.Window.End, after, kinds, criteria.AccountId, chunk), cancellationToken);
                foreach (var row in rows)
                {
                    scanned++;
                    var view = AuditEntryBuilder.Build(row, ownerEmail);
                    if (AuditEntryBuilder.Matches(view, criteria))
                    {
                        hits.Add(new AuditHit(new AuditPosition(row.At, row.Source, row.Id), view));
                    }

                    if (hits.Count > limit)
                    {
                        return new AuditScan(source.Source, hits, false, null, scanned);
                    }
                }

                if (rows.Count < chunk)
                {
                    return new AuditScan(source.Source, hits, false, null, scanned);
                }

                var last = rows[^1];
                if (scanned >= MaxScan)
                {
                    return new AuditScan(source.Source, hits, false, new AuditPosition(last.At, last.Source, last.Id), scanned);
                }

                after = new AuditAfter(last.At, true, last.Id);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AuditReadLog.SourceFailed(logger, AuditSources.Code(source.Source), ex.GetType().Name);
            return new AuditScan(source.Source, [], true, null, scanned);
        }
    }

    /// <summary>Where a store continues after the cursor: a store of a lower ordinal also gives rows at the cursor's moment (they come after it), a higher one does not (they came before).</summary>
    internal static AuditAfter AfterFor(AuditSource source, AuditPosition cursor) =>
        source == cursor.Source ? new AuditAfter(cursor.At, true, cursor.Id) : new AuditAfter(cursor.At, source < cursor.Source, null);
}
