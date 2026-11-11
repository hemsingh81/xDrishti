using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.History;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Market.History;

namespace XDrishti.Application.Market.History;

/// <summary>One instrument's history depth and backfill state (<c>xd data history</c>).</summary>
public sealed record HistoryRow(Guid InstrumentId, string Symbol, DateTimeOffset? FirstCsvBar, string Status, int ChunksDone, int ChunksFailed, int ChunksTotal, string ProbeStatus);

/// <summary>The backfill state of every tracked instrument. Reads only; needs no switch.</summary>
public sealed record ShowHistoryQuery : IQuery<IReadOnlyList<HistoryRow>>;

internal sealed class ShowHistoryHandler(IFetchTargets targets, IBackfillStore store) : IQueryHandler<ShowHistoryQuery, IReadOnlyList<HistoryRow>>
{
    public async ValueTask<IReadOnlyList<HistoryRow>> HandleAsync(ShowHistoryQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var list = await targets.ListAsync(cancellationToken);
        var seams = await store.ListSeamsAsync([.. list.Select(t => t.InstrumentId)], cancellationToken);
        var counts = (await store.CountChunksAsync(cancellationToken)).ToDictionary(c => c.InstrumentId);
        return
        [
            .. list.Select(t =>
            {
                var seam = seams.GetValueOrDefault(t.InstrumentId);
                var count = counts.GetValueOrDefault(t.InstrumentId);
                var probe = seam?.ProbeStatus ?? ProbeStatus.None;
                var status = BackfillStatus.Of(count?.Total ?? 0, count?.Finished ?? 0, count?.Failed ?? 0, probe, seam?.SeamJump ?? false);
                return new HistoryRow(t.InstrumentId, t.Symbol, seam?.SeamAt, status, count?.Finished ?? 0, count?.Failed ?? 0, count?.Total ?? 0, probe);
            }).OrderBy(r => r.Symbol, StringComparer.Ordinal),
        ];
    }
}
