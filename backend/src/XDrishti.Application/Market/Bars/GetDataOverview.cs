using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Market.Bars.Csv;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Market.Bars;

/// <summary>Everything the Data &amp; import screen shows above the table: the CSV folder, the Dhan account and whether a fetch can run, the aggregates, the stored bars with their freshness, the job in progress and the last job of each phase.</summary>
public sealed record GetDataOverviewQuery : IQuery<DataOverview>;

internal sealed class GetDataOverviewHandler(
    ICsvFolder folder,
    ICsvFileLog csvLog,
    IIntradayBarSource source,
    IDataAccountStatus account,
    IDataSettings settings,
    IBarQueries queries,
    IDataJobStore jobs,
    TimeProvider clock) : IQueryHandler<GetDataOverviewQuery, DataOverview>
{
    private static readonly string[] Ended = [PhaseStatus.Succeeded, PhaseStatus.Failed, PhaseStatus.Skipped];

    private static readonly string[] SucceededOnly = [PhaseStatus.Succeeded];

    public async ValueTask<DataOverview> HandleAsync(GetDataOverviewQuery query, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var csv = await CsvAsync(cancellationToken);
        var dhan = await DhanAsync(cancellationToken);

        var expected = DataFreshness.LastClosedSession(now);
        var expectedStart = new DateTimeOffset(expected.ToDateTime(TimeOnly.MinValue), IndiaTime.Offset);
        var totals = await queries.GetBarTotalsAsync(expectedStart, cancellationToken);
        int? behind = totals.LastBar is { } last ? DataFreshness.SessionsBehind(IndiaTime.Today(last), expected) : null;
        var bars = new BarsOverview(totals.Instruments, totals.InstrumentsBehind, totals.InstrumentsEmpty, totals.Bars, totals.FirstBar, totals.LastBar, expected, behind, behind > 0);

        var stats = await queries.GetAggregateTotalsAsync(cancellationToken);
        var byCode = stats.Timeframes.ToDictionary(t => t.Timeframe, StringComparer.Ordinal);
        var aggregates = new AggregatesOverview(
            [.. Timeframes.Aggregated.Select(t => byCode.GetValueOrDefault(t.ToCode()) ?? new TimeframeStat(t.ToCode(), 0, null))],
            stats.LastBuiltAt,
            stats.InstrumentsPending);

        var running = await jobs.GetRunningAsync(cancellationToken);
        var last3 = new LastDataJobs(
            View(await jobs.GetLastWithPhaseAsync(DataSteps.Csv, Ended, cancellationToken)),
            View(await jobs.GetLastWithPhaseAsync(DataSteps.Dhan, Ended, cancellationToken)),
            View(await jobs.GetLastWithPhaseAsync(DataSteps.Aggregate, Ended, cancellationToken)));
        var lastFetch = await jobs.GetLastWithPhaseAsync(DataSteps.Dhan, SucceededOnly, cancellationToken);
        dhan = dhan with { LastFetchAt = lastFetch?.Dhan.FinishedAt, LastFetchBars = lastFetch?.Dhan.Inserted };

        return new DataOverview(csv, dhan, aggregates, bars, View(running), last3);
    }

    private static DataJobView? View(DataJob? job) => job is null ? null : DataJobView.From(job);

    private async ValueTask<CsvOverview> CsvAsync(CancellationToken cancellationToken)
    {
        var loaded = await csvLog.ListAsync(cancellationToken);
        var exists = folder.Exists();
        var files = exists ? (await ListAsync(cancellationToken)).Where(f => CsvFileName.TryParse(f.Name, out _)).ToList() : [];
        var current = files.Count(f => loaded.TryGetValue(f.Name, out var l) && l.Size == f.Size && l.ModifiedAt == f.ModifiedAt);
        return new CsvOverview(
            folder.Path,
            exists,
            files.Count,
            current,
            files.Count - current,
            loaded.Values.Sum(l => l.Rows),
            loaded.Count == 0 ? null : loaded.Values.Max(l => l.LoadedAt));
    }

    /// <summary>A folder that exists but cannot be read (permissions of the mount) shows as having no files: the overview must still load, the CSV phase reports the problem.</summary>
    private async ValueTask<IReadOnlyList<CsvFileInfo>> ListAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await folder.ListAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private async ValueTask<DhanOverview> DhanAsync(CancellationToken cancellationToken)
    {
        var availability = await source.CheckAsync(cancellationToken);
        var state = await account.GetAsync(cancellationToken);
        var view = state.AccountId is { } id ? new DataAccountView(id, state.Name ?? string.Empty, state.Status) : null;
        var (reason, code) = !availability.Available
            ? (availability.Reason ?? "The Dhan Data API fetch is not available.", DataErrors.DhanNotAvailableCode)
            : !state.CanFetch ? (state.Reason ?? "There is no connected primary Data account.", DataErrors.DhanNotConnectedCode) : (null, null);
        return new DhanOverview(view, reason is null, reason, code, settings.DhanInitialDays, settings.DhanRequestsPerSecond, null, null);
    }
}
