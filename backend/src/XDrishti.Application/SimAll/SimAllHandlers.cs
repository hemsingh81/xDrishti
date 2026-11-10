using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Abstractions.SimAll;
using XDrishti.Application.Lab;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Checklist;
using XDrishti.Domain.Common;
using XDrishti.Domain.Research.SimAll;

namespace XDrishti.Application.SimAll;

/// <summary>
/// Starts the one simulate-everything job (spec AU-12, ADR 0243, ADR 0248). Fail closed, in this order: the owner, the backfill (complete or no job), the training window (ends before the hold-out and its embargo; none determinable means no job), then the chunks: the latest version of every
/// stored strategy over every tracked instrument with stored history. The job row and all its chunks are written in one transaction; a second start while one is active is a conflict. This handler runs nothing and places nothing; the loop of the host works the chunks.
/// </summary>
internal sealed class StartSimAllHandler(
    ISimAllBackfillGate backfill,
    TrainingWindowResolver window,
    ISimAllUniverse universe,
    IStrategyStore strategies,
    ISimAllStore store,
    SimAllSettings settings,
    IAccountEventLog events,
    IAccountUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<StartSimAllHandler> logger) : ICommandHandler<StartSimAllCommand, SimAllView>
{
    public async ValueTask<Result<SimAllView>> HandleAsync(StartSimAllCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!OwnerConfirmation.IsOwnerActor(command.Actor) || command.Actor.Length > 100)
        {
            return Refuse(SimAllErrors.OwnerOnly);
        }

        if (await store.GetActiveAsync(cancellationToken) is { } active)
        {
            return Refuse(SimAllErrors.Active(active.Id));
        }

        if (await backfill.IncompleteReasonAsync(cancellationToken) is { } reason)
        {
            return Refuse(SimAllErrors.BackfillIncomplete(reason));
        }

        var training = await window.ResolveAsync(null, null, cancellationToken);
        if (training.IsFailure)
        {
            return Refuse(training.Error);
        }

        var instruments = await universe.InstrumentIdsAsync(cancellationToken);
        if (instruments.Count == 0)
        {
            return Refuse(SimAllErrors.NothingToRun("No tracked instrument has stored history."));
        }

        var versions = new List<StoredVersion>();
        foreach (var family in (await strategies.ListAsync(cancellationToken)).OrderBy(s => s.Id, StringComparer.Ordinal))
        {
            if (await strategies.GetVersionAsync(family.Id, null, cancellationToken) is { } latest)
            {
                versions.Add(latest);
            }
        }

        if (versions.Count == 0)
        {
            return Refuse(SimAllErrors.NothingToRun("No strategy is stored."));
        }

        var now = clock.GetUtcNow();
        var chunks = versions.SelectMany(v => instruments.Select(i => (Version: v, Instrument: i))).Select((x, n) => new NewSimAllChunk(x.Version.StrategyId, x.Version.Version, x.Version.Hash, x.Instrument, n + 1)).ToList();
        var job = new SimAllJob(Guid.CreateVersion7(now), SimAllStates.Running, null, training.Value.From, training.Value.To, training.Value.HoldOutStart, settings.Workers, command.Actor, now, now, null);
        var inserted = await store.InsertAsync(new NewSimAllJob(job, chunks), cancellationToken);
        if (!inserted.Created)
        {
            return Refuse(SimAllErrors.Active(inserted.Job.Id));
        }

        SimAllLog.Started(logger, job.Id, chunks.Count, versions.Count);
        events.Record(AccountEvent.Record(AccountEventType.SimulateAllStarted, null, $"chunks={chunks.Count} strategies={versions.Count}", command.Actor, now));
        await unitOfWork.TrySaveChangesAsync(cancellationToken);
        return Result.Success(SimAllView.Of(new SimAllSnapshot(job, new SimAllCounts(chunks.Count, 0, 0, 0, chunks.Count, 0, 0, 0))));
    }

    private Result<SimAllView> Refuse(Error error)
    {
        SimAllLog.Refused(logger, error.Code);
        return Result.Failure<SimAllView>(error);
    }
}

/// <summary>Pauses the running job. The workers stop claiming at once; a chunk in flight finishes. The owner resumes it.</summary>
internal sealed class PauseSimAllHandler(ISimAllStore store, IAccountEventLog events, IAccountUnitOfWork unitOfWork, TimeProvider clock, ILogger<PauseSimAllHandler> logger) : ICommandHandler<PauseSimAllCommand, SimAllView>
{
    public async ValueTask<Result<SimAllView>> HandleAsync(PauseSimAllCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!OwnerConfirmation.IsOwnerActor(command.Actor))
        {
            return Result.Failure<SimAllView>(SimAllErrors.OwnerOnly);
        }

        if (await store.GetActiveAsync(cancellationToken) is not { } job)
        {
            return Result.Failure<SimAllView>(SimAllErrors.NoActiveJob);
        }

        var now = clock.GetUtcNow();
        if (job.State == SimAllStates.Paused && job.PauseReason == SimAllPause.Owner)
        {
            return await SimAllRead.CurrentAsync(store, cancellationToken); // paused by the owner already
        }

        if (!await store.PauseAsync(job.Id, SimAllPause.Owner, now, cancellationToken))
        {
            return Result.Failure<SimAllView>(SimAllErrors.NotPausable);
        }

        SimAllLog.Paused(logger, job.Id, SimAllPause.Owner);
        events.Record(AccountEvent.Record(AccountEventType.SimulateAllPaused, null, null, command.Actor, now));
        await unitOfWork.TrySaveChangesAsync(cancellationToken);
        return await SimAllRead.CurrentAsync(store, cancellationToken);
    }
}

/// <summary>Resumes a paused job (whatever the reason). The backfill is checked again; a conditions-based pause (hours, disk) that still holds pauses the job again at the next look.</summary>
internal sealed class ResumeSimAllHandler(ISimAllStore store, ISimAllBackfillGate backfill, IAccountEventLog events, IAccountUnitOfWork unitOfWork, TimeProvider clock, ILogger<ResumeSimAllHandler> logger) : ICommandHandler<ResumeSimAllCommand, SimAllView>
{
    public async ValueTask<Result<SimAllView>> HandleAsync(ResumeSimAllCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!OwnerConfirmation.IsOwnerActor(command.Actor))
        {
            return Result.Failure<SimAllView>(SimAllErrors.OwnerOnly);
        }

        if (await store.GetActiveAsync(cancellationToken) is not { } job)
        {
            return Result.Failure<SimAllView>(SimAllErrors.NoActiveJob);
        }

        if (job.State != SimAllStates.Paused)
        {
            return Result.Failure<SimAllView>(SimAllErrors.NotPaused);
        }

        if (await backfill.IncompleteReasonAsync(cancellationToken) is { } reason)
        {
            return Result.Failure<SimAllView>(SimAllErrors.BackfillIncomplete(reason));
        }

        var now = clock.GetUtcNow();
        if (!await store.ResumeAsync(job.Id, null, now, cancellationToken))
        {
            return Result.Failure<SimAllView>(SimAllErrors.NotPaused);
        }

        SimAllLog.Resumed(logger, job.Id);
        events.Record(AccountEvent.Record(AccountEventType.SimulateAllResumed, null, null, command.Actor, now));
        await unitOfWork.TrySaveChangesAsync(cancellationToken);
        return await SimAllRead.CurrentAsync(store, cancellationToken);
    }
}

/// <summary>Cancels the active job: chunks not done stay pending and are never run. Stored runs stay.</summary>
internal sealed class CancelSimAllHandler(ISimAllStore store, IAccountEventLog events, IAccountUnitOfWork unitOfWork, TimeProvider clock, ILogger<CancelSimAllHandler> logger) : ICommandHandler<CancelSimAllCommand, SimAllView>
{
    public async ValueTask<Result<SimAllView>> HandleAsync(CancelSimAllCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!OwnerConfirmation.IsOwnerActor(command.Actor))
        {
            return Result.Failure<SimAllView>(SimAllErrors.OwnerOnly);
        }

        if (await store.GetActiveAsync(cancellationToken) is not { } job)
        {
            return Result.Failure<SimAllView>(SimAllErrors.NoActiveJob);
        }

        var now = clock.GetUtcNow();
        if (!await store.CancelAsync(job.Id, now, cancellationToken))
        {
            return Result.Failure<SimAllView>(SimAllErrors.NoActiveJob);
        }

        SimAllLog.Cancelled(logger, job.Id);
        events.Record(AccountEvent.Record(AccountEventType.SimulateAllCancelled, null, null, command.Actor, now));
        await unitOfWork.TrySaveChangesAsync(cancellationToken);
        return await SimAllRead.CurrentAsync(store, cancellationToken);
    }
}

/// <summary>Reads the active job, or the newest one when none is active (404 when there never was one). Read only.</summary>
internal sealed class GetSimAllHandler(ISimAllStore store) : IQueryHandler<GetSimAllQuery, Result<SimAllView>>
{
    public async ValueTask<Result<SimAllView>> HandleAsync(GetSimAllQuery query, CancellationToken cancellationToken) =>
        await SimAllRead.CurrentAsync(store, cancellationToken);
}

internal static class SimAllRead
{
    public static async ValueTask<Result<SimAllView>> CurrentAsync(ISimAllStore store, CancellationToken cancellationToken) =>
        await store.GetCurrentAsync(cancellationToken) is { } snapshot ? Result.Success(SimAllView.Of(snapshot)) : Result.Failure<SimAllView>(SimAllErrors.NoActiveJob);
}
