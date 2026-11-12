using System.Text.Json.Serialization;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.History;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.History;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Market.History;

/// <summary>The switch, read-only: the owner cannot turn it on from the web (CF-04e decision D1).</summary>
public sealed record HistorySwitchView(bool Enabled, bool Editable, string? Reason);

public sealed record HistoryChunksView(int Done, int Failed, int Total);

public sealed record HistoryProbeView(string Status, DateTimeOffset? At, double? MedianBps, double? WithinMaxPct);

public sealed record HistoryInstrumentView(Guid InstrumentId, string Symbol, DateTimeOffset? HistoryFrom, DateTimeOffset? FirstCsvBar, string BackfillStatus, HistoryChunksView Chunks, HistoryProbeView Probe, double? SeamJumpPct);

public sealed record HistoryByStatusView(
    int None,
    int Planned,
    int Partial,
    int Done,
    [property: JsonPropertyName("seam_unresolved")] int SeamUnresolved,
    [property: JsonPropertyName("seam_jump")] int SeamJump);

public sealed record HistoryDepthView(int Under2, int From2to3, int From3to5, int From5);

public sealed record HistoryTotalsView(int Instruments, HistoryByStatusView ByStatus, HistoryChunksView Chunks, HistoryDepthView Depth);

public sealed record HistoryDailyView(DateOnly From, DateOnly To);

public sealed record HistoryOverview(HistorySwitchView Switch, string RunWindow, bool InWindow, bool Running, string ServiceId, HistoryDailyView? Daily, HistoryTotalsView Totals, IReadOnlyList<HistoryInstrumentView> Instruments);

/// <summary>The history card of the Data screen (CF-04e): depth and backfill state of every tracked instrument, the switch (read-only), the window and whether the service runs. Reads only; makes no request.</summary>
public sealed record GetHistoryOverviewQuery : IQuery<HistoryOverview>;

internal sealed class GetHistoryOverviewHandler(IFetchTargets targets, IBackfillStore store, IBackfillSettings settings, IBackfillCalendar calendar, IServiceRunStore runs, TimeProvider clock) : IQueryHandler<GetHistoryOverviewQuery, HistoryOverview>
{
    public const string OffReason = "Data:History:Backfill:Enabled is off (a deployment setting). It stays off until source-aware replay (CF-04d) is built.";

    // Part A's daily files reach back at most this far; the span is read once for the whole set.
    private const int DailyLookbackDays = 6 * 366;

    public async ValueTask<HistoryOverview> HandleAsync(GetHistoryOverviewQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var now = clock.GetUtcNow();
        var list = await targets.ListAsync(cancellationToken);
        var seams = await store.ListSeamsAsync([.. list.Select(t => t.InstrumentId)], cancellationToken);
        var counts = (await store.CountChunksAsync(cancellationToken)).ToDictionary(c => c.InstrumentId);

        var rows = new List<HistoryInstrumentView>(list.Count);
        foreach (var t in list.OrderBy(t => t.Symbol, StringComparer.Ordinal))
        {
            var seam = seams.GetValueOrDefault(t.InstrumentId);
            var count = counts.GetValueOrDefault(t.InstrumentId);
            var probe = seam?.ProbeStatus ?? ProbeStatus.None;
            var status = BackfillStatus.Of(count?.Total ?? 0, count?.Finished ?? 0, count?.Failed ?? 0, probe, seam?.SeamJump ?? false);
            rows.Add(new HistoryInstrumentView(
                t.InstrumentId,
                t.Symbol,
                t.FirstBar,
                seam?.SeamAt,
                status,
                new HistoryChunksView(count?.Finished ?? 0, count?.Failed ?? 0, count?.Total ?? 0),
                new HistoryProbeView(probe, seam?.ProbedAt, seam?.MedianBps, seam?.WithinMaxPct),
                seam?.SeamJump == true ? seam.JumpPct : null));
        }

        var years = list.Where(t => t.FirstBar is not null && t.LastBar is not null).Select(t => (IndiaTime.Today(t.LastBar!.Value).DayNumber - IndiaTime.Today(t.FirstBar!.Value).DayNumber) / HistoryDepth.DaysPerYear).ToList();
        var (u2, f2, f3, f5) = HistoryDepth.Buckets(years);
        var totals = new HistoryTotalsView(
            rows.Count,
            new HistoryByStatusView(Count(rows, BackfillStatus.None), Count(rows, BackfillStatus.Planned), Count(rows, BackfillStatus.Partial), Count(rows, BackfillStatus.Done), Count(rows, BackfillStatus.SeamUnresolved), Count(rows, BackfillStatus.SeamJump)),
            new HistoryChunksView(rows.Sum(r => r.Chunks.Done), rows.Sum(r => r.Chunks.Failed), rows.Sum(r => r.Chunks.Total)),
            new HistoryDepthView(u2, f2, f3, f5));

        var today = IndiaTime.Today(now);
        var known = await calendar.KnownDaysAsync(today.AddDays(-DailyLookbackDays), today, cancellationToken);
        var running = (await runs.GetRunningServicesAsync(cancellationToken)).Contains(HistoryBackfillService.Id);
        return new HistoryOverview(
            new HistorySwitchView(settings.Enabled, false, settings.Enabled ? null : OffReason),
            Window(settings),
            HistoryBackfillService.InWindow(now, settings.RunWindowFrom, settings.RunWindowTo),
            running,
            HistoryBackfillService.Id,
            known.Count == 0 ? null : new HistoryDailyView(known.Min(), known.Max()),
            totals,
            rows);
    }

    public static string Window(IBackfillSettings settings) => $"{settings.RunWindowFrom:HH\\:mm}-{settings.RunWindowTo:HH\\:mm}";

    private static int Count(List<HistoryInstrumentView> rows, string status) => rows.Count(r => r.BackfillStatus == status);
}
