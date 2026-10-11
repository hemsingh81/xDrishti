using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Market.Import;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;

namespace XDrishti.Application.Market.Refresh;

/// <summary>Carries out a registered refresh: download, read, enrich, save. Runs in the background; the run record shows progress and result.</summary>
public sealed record ExecuteRefreshCommand(Guid RunId) : ICommand;

internal sealed class ExecuteRefreshHandler(
    IRefreshRunStore runs,
    IInstrumentSource source,
    IInstrumentCatalog catalog,
    TimeProvider clock,
    ILogger<ExecuteRefreshHandler> logger) : ICommandHandler<ExecuteRefreshCommand>
{
    private const string UnexpectedFailure = "The refresh failed unexpectedly; the list was not changed. See the API log.";

    public async ValueTask<Result> HandleAsync(ExecuteRefreshCommand command, CancellationToken cancellationToken)
    {
        var run = await runs.GetAsync(command.RunId, cancellationToken);
        if (run is null || !run.IsRunning)
        {
            return Result.Success();
        }

        try
        {
            await RunAsync(run, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await FailAsync(run, "The refresh was interrupted because the application is stopping.", null, CancellationToken.None);
        }
#pragma warning disable CA1031 // any failure must end the run record as failed (and leave the list untouched), never crash the worker
        catch (Exception ex)
#pragma warning restore CA1031
        {
            MarketLog.RefreshCrashed(logger, run.Id, ex);
            await FailAsync(run, UnexpectedFailure, null, CancellationToken.None);
        }

        return Result.Success();
    }

    private async ValueTask RunAsync(RefreshRun run, CancellationToken cancellationToken)
    {
        // The "file unchanged" shortcut is only trusted when there is a list to keep, and the last import worked under today's rules with sectors applied.
        var known = run.Force || await catalog.CountActiveAsync(cancellationToken) == 0 ? null : await runs.GetLastStampsAsync(RefreshRules.Version, cancellationToken);
        await ProgressAsync(run, RefreshPhase.Downloading, 0, cancellationToken);

        SourceStamp dhanStamp;
        MasterParse parse;
        await using (var master = await source.OpenMasterAsync(known?.Dhan, cancellationToken))
        {
            if (master.Kind == SourceDownloadKind.Failed)
            {
                await FailAsync(run, master.Failure!, null, cancellationToken);
                return;
            }

            if (master.Kind == SourceDownloadKind.NotModified)
            {
                var dhan = master.Stamp.IsEmpty ? known?.Dhan ?? SourceStamp.None : master.Stamp;
                run.MarkUnchanged(clock.GetUtcNow(), dhan, known?.Sector ?? SourceStamp.None, await catalog.CountActiveAsync(cancellationToken));
                await runs.SaveAsync(cancellationToken);
                MarketLog.RefreshUnchanged(logger, run.Id);
                return;
            }

            dhanStamp = master.Stamp;
            await ProgressAsync(run, RefreshPhase.Parsing, 0, cancellationToken);
            Result<MasterParse> parsed;
            try
            {
                parsed = await DhanMasterParser.ParseAsync(
                    master.Content!, rows => runs.ReportProgressAsync(run.Id, RefreshPhase.Parsing, rows, cancellationToken), cancellationToken);
            }
            catch (IOException ex)
            {
                parsed = Result.Failure<MasterParse>(Error.Upstream(DhanMasterParser.FailureCode, ReadFailure(ex)));
            }

            if (parsed.IsFailure)
            {
                await FailAsync(run, parsed.Error.Message, null, cancellationToken);
                return;
            }

            parse = parsed.Value;
        }

        await ProgressAsync(run, RefreshPhase.Enriching, parse.RowsRead, cancellationToken);
        var warnings = new List<string>(parse.Warnings);
        var (sectors, sectorStamp) = await ReadSectorsAsync(run, warnings, cancellationToken);

        var today = IndiaTime.Today(clock.GetUtcNow());
        var build = CatalogBuilder.Build(parse.Rows, sectors, today);
        warnings.AddRange(build.Warnings);
        if (build.Candidates.Count == 0)
        {
            await FailAsync(run, "Dhan's file has no instruments in scope. Nothing was imported.", warnings, cancellationToken);
            return;
        }

        // Tripwire: a file that would retire more than half of the list is more likely broken than right.
        var active = await catalog.CountActiveAsync(cancellationToken);
        if (!run.Force && active > 0 && build.Candidates.Count * 2 < active)
        {
            MarketLog.RefreshTripwire(logger, run.Id, build.Candidates.Count, active);
            await FailAsync(
                run,
                $"The new list has only {build.Candidates.Count} instruments while {active} are active; it was not applied. If the file is right, run a forced refresh.",
                warnings,
                cancellationToken);
            return;
        }

        await ProgressAsync(run, RefreshPhase.Saving, parse.RowsRead, cancellationToken);
        var applied = await catalog.ApplyAsync(build.Candidates, parse.Protected, sectors is not null, clock.GetUtcNow(), cancellationToken);

        var outcome = new RefreshOutcome(
            applied.Added,
            applied.Updated,
            applied.Retired,
            applied.Revived,
            applied.Unchanged,
            (int)Math.Min(int.MaxValue, parse.OutOfScope + build.Expired),
            build.Collisions,
            build.SectorMatched,
            build.SectorUnmatched,
            build.FnoUnderlyings,
            applied.TotalActive,
            parse.RowsRead,
            sectors is not null,
            build.KeptByType,
            warnings);
        run.Succeed(clock.GetUtcNow(), dhanStamp, sectorStamp, outcome);
        await runs.SaveAsync(cancellationToken);
        MarketLog.RefreshSucceeded(logger, run.Id, applied.Added, applied.Updated, applied.Retired, applied.Revived, applied.Unchanged, applied.TotalActive);
    }

    /// <summary>The sector list, or null (and a warning) when it cannot be used for any reason: the sectors stay as they are then and the run goes on.</summary>
    private async ValueTask<(SectorList? Sectors, SourceStamp Stamp)> ReadSectorsAsync(RefreshRun run, List<string> warnings, CancellationToken cancellationToken)
    {
        try
        {
            await using var download = await source.OpenSectorsAsync(cancellationToken);
            if (download.Kind != SourceDownloadKind.Content)
            {
                return Unusable(run, warnings, download.Failure ?? "the list was not delivered");
            }

            var parsed = await SectorList.ParseAsync(download.Content!, cancellationToken);
            return parsed.IsSuccess ? (parsed.Value, download.Stamp) : Unusable(run, warnings, parsed.Error.Message);
        }
        catch (IOException ex)
        {
            return Unusable(run, warnings, ReadFailure(ex));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // the sector list is optional: whatever goes wrong with it (timeout, refused address, bad data) only costs the sectors
        catch (Exception)
#pragma warning restore CA1031
        {
            return Unusable(run, warnings, "the list could not be fetched");
        }
    }

    private static string ReadFailure(IOException ex) =>
        ex is SourceReadException ? ex.Message : "The download was interrupted.";

    private (SectorList? Sectors, SourceStamp Stamp) Unusable(RefreshRun run, List<string> warnings, string reason)
    {
        MarketLog.SectorsSkipped(logger, run.Id, reason);
        warnings.Add($"Sectors were not updated: {reason}");
        return (null, SourceStamp.None);
    }

    private async ValueTask ProgressAsync(RefreshRun run, RefreshPhase phase, long rows, CancellationToken cancellationToken)
    {
        run.ReportProgress(phase, rows);
        await runs.ReportProgressAsync(run.Id, phase, rows, cancellationToken);
    }

    private async ValueTask FailAsync(RefreshRun run, string error, IReadOnlyList<string>? warnings, CancellationToken cancellationToken)
    {
        if (!run.IsRunning)
        {
            return; // it ended (for example saving its result failed): nothing to record twice
        }

        run.Fail(clock.GetUtcNow(), error, warnings);
        await runs.SaveAsync(cancellationToken);
        MarketLog.RefreshFailed(logger, run.Id, error);
    }
}
