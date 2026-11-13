using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Lab;
using XDrishti.Application.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Learning.CellsRefresh;

/// <summary>
/// The scheduled refresh of the cell statistics (spec LC-01, ADR 0277). Repeats the newest snapshot: the same sources and options, the same splits. A source whose version has a pre-registration and whose window ends before the refresh end is simulated again over the exact request with only
/// the end moved (stored as a research run, one <c>cells_refresh</c> ledger row each); every other source is kept. The refresh end is the earlier of the last closed session and the hold-out start less the embargo; with no block it fails closed. All or nothing: every simulation is
/// made before anything is stored, any failure ends the run with its code and no snapshot, and nothing is re-stamped. Reads stored runs through <see cref="IRunStore"/> only and no shadow or paper result. It names no order, approval or arming port.
/// </summary>
internal sealed class RefreshCellStatsHandler(
    IRunStore runs,
    ICellStatsStore cells,
    IHoldOutStore holdOut,
    HoldOutOptions holdOutOptions,
    ITradingCalendar tradingCalendar,
    IStrategyStore strategies,
    IRegistrationStore registrations,
    ISourceRequestPlanner planner,
    IQueryHandler<RunSimulationQuery, Result<SimulationRun>> simulation,
    ICommandHandler<StoreRunCommand, StoreOutcome> storeRun,
    IRunRecordWriter recordWriter,
    ITestLedger ledger,
    ICommandHandler<ComputeCellStatsCommand, CellStatsOutcome> compute,
    TimeProvider time,
    ILogger<RefreshCellStatsHandler> logger) : ICommandHandler<RefreshCellStatsCommand, RefreshCellStatsOutcome>
{
    private sealed record Pending(CellSource Source, StoredVersion Version, SimulationRun Run, RunRecordText Record);

    public async ValueTask<Result<RefreshCellStatsOutcome>> HandleAsync(RefreshCellStatsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var template = CashCells.Only(await cells.ListLatestCashAsync(null, null, cancellationToken), "cells-refresh", logger); // cash cells only (ADR 0193); the regime and tier rows below are only looked at for their snapshot key
        if (template.Count == 0)
        {
            return Fail(CellsRefreshErrors.NoSnapshot(null));
        }

        var newest = template[0];
        if (CellSource.TryParseList(newest.Sources) is not { } sources)
        {
            return Fail(CellsRefreshErrors.TemplateUnreadable(null));
        }

        if (sources.Count > ComputeCellStatsHandler.MaxSources)
        {
            return Fail(CellsRefreshErrors.TooManySources(sources.Count, null));
        }

        var now = time.GetUtcNow();
        var calendar = await tradingCalendar.GetAsync(cancellationToken);
        var session = calendar.LastClosedSession(now);
        var block = await holdOut.CurrentAsync(cancellationToken);
        var released = await holdOut.ReleasedAsync(cancellationToken); // read before anything is written: a failed read ends the run with nothing stored
        if (CellsRefreshRules.RefreshEnd(session, block?.Start, holdOutOptions.TrainingEmbargoDays) is not { } end)
        {
            return Fail(CellsRefreshErrors.NoHoldOut(null));
        }

        // phase 1: decide for every source; simulate what is extended, store nothing yet. shortcut: up to 50 simulated runs (the cap of one snapshot) are held in memory at once so that a failed one leaves nothing stored; the run is after eod, outside market hours
        var kept = new List<string>();
        var pending = new List<Pending>();
        foreach (var source in sources)
        {
            var header = await runs.FindAsync(source.RunKey, cancellationToken);
            if (header is null)
            {
                return Fail(CellsRefreshErrors.SourceFailed(source.RunKey, LearningErrors.RunNotFoundCode, end));
            }

            var version = await strategies.GetByHashAsync(header.DefinitionSha256, cancellationToken);
            var registered = version is not null && await registrations.GetAsync(version.StrategyId, version.Version, cancellationToken) is not null;
            switch (CellsRefreshRules.Decide(header.To, end, registered))
            {
                case RefreshSourceAction.UpToDate:
                    kept.Add($"{Short(source.RunKey)} kept: up_to_date");
                    continue;
                case RefreshSourceAction.NotRegistered:
                    kept.Add($"{Short(source.RunKey)} kept: not_registered");
                    continue;
            }

            var record = await runs.ReadRecordTextAsync(source.RunKey, cancellationToken);
            if (record is null)
            {
                return Fail(CellsRefreshErrors.NotRebuildable(source.RunKey, "its record is not stored", end));
            }

            var request = await planner.PlanAsync(header, record, cancellationToken);
            if (request.IsFailure)
            {
                return Fail(CellsRefreshErrors.NotRebuildable(source.RunKey, request.Error.Message, end));
            }

            var simulated = await simulation.HandleAsync(new RunSimulationQuery(request.Value with { To = end, Purpose = RunPurpose.Research }), cancellationToken);
            if (simulated.IsFailure)
            {
                return Fail(CellsRefreshErrors.SourceFailed(source.RunKey, simulated.Error.Code, end));
            }

            pending.Add(new Pending(source, version!, simulated.Value, recordWriter.Write(simulated.Value)));
        }

        // phase 2: store the new runs and count each one that is new
        var finalSources = new List<CellSource>();
        var extended = new List<string>();
        var newRuns = 0;
        var byOld = pending.ToDictionary(p => p.Source.RunKey, StringComparer.Ordinal);
        foreach (var source in sources)
        {
            if (!byOld.TryGetValue(source.RunKey, out var p))
            {
                finalSources.Add(source);
                continue;
            }

            var stored = await storeRun.HandleAsync(new StoreRunCommand(p.Run, p.Record, command.Actor), cancellationToken);
            if (stored.IsFailure)
            {
                return Fail(CellsRefreshErrors.SourceFailed(source.RunKey, stored.Error.Code, end));
            }

            if (!stored.Value.WasAlreadyStored)
            {
                newRuns++;
            }

            // shortcut: the ledger row follows the store without a shared transaction. It is appended for every extended source: the row id comes from the run key and the insert is ON CONFLICT DO NOTHING, so a repeat adds nothing and a look lost to a failed append is counted by the retry
            await ledger.AppendAsync(new LedgerRow(Guid.ParseExact(p.Run.RunKey[..32], "N"), p.Run.RunKey, p.Version.StrategyId, p.Version.Version, p.Run.Strategy.DefinitionSha256, null, p.Run.Universe.Label, p.Run.From, p.Run.To, CellsRefreshRules.TrialKind, now, CellsRefreshRules.TrialKind), cancellationToken);

            extended.Add($"{Short(source.RunKey)} -> {Short(p.Run.RunKey)}");
            finalSources.Add(source with { RunKey = p.Run.RunKey });
        }

        // phase 3: the snapshot, as of the last closed session, with the splits of the template
        var regime = (await cells.ListRegimeLatestAsync(null, null, cancellationToken)).Any(r => r.SnapshotKey == newest.SnapshotKey);
        var universe = (await cells.ListUniverseLatestAsync(null, null, cancellationToken)).Any(r => r.SnapshotKey == newest.SnapshotKey && r.Row.Cell.Universe != "*");
        var computed = await compute.HandleAsync(new ComputeCellStatsCommand(session, finalSources, newest.Options, command.Actor, regime, universe), cancellationToken);
        if (computed.IsFailure)
        {
            return Fail(CellsRefreshErrors.Wrap(computed.Error, end));
        }

        CellsRefreshLog.Completed(logger, computed.Value.SnapshotKey[..12], kept.Count, extended.Count, newRuns);
        return Result.Success(new RefreshCellStatsOutcome(session, end, computed.Value.SnapshotKey, computed.Value.AlreadyStored, kept, extended, newRuns, Domain.Lab.SeenHoldOut.Labels(released, DateOnly.MinValue, end)));
    }

    private Result<RefreshCellStatsOutcome> Fail(Error error)
    {
        CellsRefreshLog.Refused(logger, error.Code);
        return Result.Failure<RefreshCellStatsOutcome>(error);
    }

    private static string Short(string key) => key.Length <= 12 ? key : key[..12];
}
