using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.History;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Market.Actions;
using XDrishti.Domain.Market.History;

namespace XDrishti.Application.Market.History;

/// <summary>
/// One instrument's history depth and backfill state (<c>xd data history</c>). <paramref name="Verified"/> is <c>yes</c> when the instrument's pre-seam bars would be admitted to a run (CF-04d), else the first failing reason (<see cref="SeamVerification"/>, or <c>no_seam</c> for an
/// instrument with no seam row); it says what the instrument is whatever the switch, which is shown on its own.
/// </summary>
public sealed record HistoryRow(Guid InstrumentId, string Symbol, DateTimeOffset? FirstCsvBar, string Status, int ChunksDone, int ChunksFailed, int ChunksTotal, string ProbeStatus, string Verified = "no_seam");

/// <summary>The backfill state of every tracked instrument. Reads only; needs no switch.</summary>
public sealed record ShowHistoryQuery : IQuery<IReadOnlyList<HistoryRow>>;

internal sealed class ShowHistoryHandler(IFetchTargets targets, IBackfillStore store, IBackfillSeams seams, IAdjustmentFactors adjustments) : IQueryHandler<ShowHistoryQuery, IReadOnlyList<HistoryRow>>
{
    public const string Yes = "yes";

    public async ValueTask<IReadOnlyList<HistoryRow>> HandleAsync(ShowHistoryQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var list = await targets.ListAsync(cancellationToken);
        var records = await store.ListSeamsAsync([.. list.Select(t => t.InstrumentId)], cancellationToken);
        var counts = (await store.CountChunksAsync(cancellationToken)).ToDictionary(c => c.InstrumentId);
        var rows = new List<HistoryRow>(list.Count);
        foreach (var t in list)
        {
            var seam = records.GetValueOrDefault(t.InstrumentId);
            var count = counts.GetValueOrDefault(t.InstrumentId);
            var probe = seam?.ProbeStatus ?? ProbeStatus.None;
            var status = BackfillStatus.Of(count?.Total ?? 0, count?.Finished ?? 0, count?.Failed ?? 0, probe, seam?.SeamJump ?? false);
            var verified = seam is null
                ? PreSeamAdmission.NoSeam
                : await VerifiedAsync(t.InstrumentId, cancellationToken);
            rows.Add(new HistoryRow(t.InstrumentId, t.Symbol, seam?.SeamAt, status, count?.Finished ?? 0, count?.Failed ?? 0, count?.Total ?? 0, probe, verified));
        }

        return [.. rows.OrderBy(r => r.Symbol, StringComparer.Ordinal)];
    }

    private async ValueTask<string> VerifiedAsync(Guid id, CancellationToken cancellationToken)
    {
        var decision = await PreSeamAdmission.DecideAsync(seams, id, await adjustments.GetAsync(id, cancellationToken), await adjustments.CsvAdjustedThroughAsync(id, cancellationToken), true, cancellationToken);
        return decision.IsAdmitted ? Yes : decision.Reason ?? PreSeamAdmission.NoSeam;
    }
}
