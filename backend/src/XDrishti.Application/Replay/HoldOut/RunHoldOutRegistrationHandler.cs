using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Replay;
using XDrishti.Application.Lab;
using XDrishti.Domain.Common;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Lab;

namespace XDrishti.Application.Replay.HoldOut;

/// <summary>The one run of a registration (SR-03 D4). <c>Mode</c> is <c>owner</c> (the password was checked at the endpoint) or <c>auto</c> (the seal step, with its settings snapshot); <c>SwitchStamp</c> names the <c>auto_run</c> switch row that allowed an auto run (id and time), kept with the look.</summary>
public sealed record RunHoldOutRegistrationCommand(Guid RegistrationId, string Mode, string Actor, string? SwitchStamp = null) : ICommand;

/// <summary>The ids of the two arm results the run will store.</summary>
public sealed record StartedHoldOutRun(IReadOnlyList<Guid> RunIds);

/// <summary>
/// Claims the <c>system</c> look of the registration's block (SR-03 D4). Before anything is claimed: the registration must be the newest for its block and unused, the block must be stored, in force and sealed, and every stored input must still resolve
/// (409 <c>replay.registration_drift</c> otherwise, nothing burned). Then the look is stored through HO-01's insert-only store, burned even if the run is abandoned; a second run answers 409 <c>replay.holdout_used</c>. The block is read afterwards by
/// <see cref="HoldOutSystemRunner"/>, which stands on the claim. Reads no bar and places no order.
/// </summary>
internal sealed class RunHoldOutRegistrationHandler(
    IHoldOutStore blocks,
    IHoldOutRegistrationStore store,
    HoldOutEpochs epochs,
    HoldOutInputCheck inputs,
    ITestLedger ledger,
    HoldOutOptions options,
    TimeProvider time) : ICommandHandler<RunHoldOutRegistrationCommand, StartedHoldOutRun>
{
    public async ValueTask<Result<StartedHoldOutRun>> HandleAsync(RunHoldOutRegistrationCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Mode is not (RegisterHoldOutHandler.OwnerMode or RegisterHoldOutHandler.AutoMode) || string.IsNullOrWhiteSpace(command.Actor) || command.Actor.Length > 100)
        {
            return Result.Failure<StartedHoldOutRun>(HoldOutRegistrationErrors.Invalid("bad_mode", "a run is owner or auto and needs an actor of at most 100 characters"));
        }

        if (await store.GetAsync(command.RegistrationId, cancellationToken) is not { } registration)
        {
            return Result.Failure<StartedHoldOutRun>(HoldOutRegistrationErrors.NotFound(command.RegistrationId));
        }

        var runIds = new[] { HoldOutRegistrationIds.ArmId(registration.Id, "selection"), HoldOutRegistrationIds.ArmId(registration.Id, "baseline") };
        if (await store.GetResultAsync(registration.Id, cancellationToken) is { } done)
        {
            return Result.Failure<StartedHoldOutRun>(HoldOutRegistrationErrors.Used($"This registration has run already ({done.Verdict}); the stored result is unchanged (GET /api/replay/holdout-registrations/{registration.Id:D})."));
        }

        var key = HoldOutRegistrationIds.LookKey(registration.Id);
        if (await blocks.GetUnlockAsync(key, HoldOutRegistrationIds.LookVersion, HoldOutHorizon.SystemKind, registration.BlockStart, cancellationToken) is not null)
        {
            return Result.Failure<StartedHoldOutRun>(HoldOutRegistrationErrors.Used("The system look of this registration is claimed already; the run is in progress or abandoned and cannot be taken again."));
        }

        if (await store.NewestForBlockAsync(registration.BlockStart, cancellationToken) is { } newest && newest.Id != registration.Id)
        {
            return Result.Failure<StartedHoldOutRun>(Error.Conflict(HoldOutRegistrationErrors.SupersededCode, "A newer registration targets this block; only the newest stored before the block begins is run."));
        }

        var chain = await blocks.ListBlocksAsync(cancellationToken);
        if (chain.FirstOrDefault(b => b.Block.Start == registration.BlockStart) is not { } stored)
        {
            return Result.Failure<StartedHoldOutRun>(LabErrors.HoldOutUnavailable("The target block is not stored yet: the block before it has not been released."));
        }

        var block = stored.Block;
        if (stored.Release is not null || chain.First(b => b.Release is null).Block.Start != block.Start)
        {
            return Result.Failure<StartedHoldOutRun>(LabErrors.HoldOutUnavailable("The target block is not the block in force (it was released, or an earlier block is still in force)."));
        }

        if (!await epochs.IsSealedAsync(block.HistoryLast, cancellationToken))
        {
            return Result.Failure<StartedHoldOutRun>(LabErrors.HoldOutFilling(block.Start, block.HistoryLast));
        }

        var config = ReplayJson.Read<HoldOutRegistrationConfig>(registration.ConfigJson);
        if (config is null)
        {
            return Result.Failure<StartedHoldOutRun>(HoldOutRegistrationErrors.Drift("The stored registration cannot be read."));
        }

        // the reader ends where the STORED block ends: a block that is not the one registered (a changed BlockMonths between registration and release) is drift, before anything is claimed
        if (config.BlockStart != block.Start || config.BlockEnd != block.HistoryLast)
        {
            return Result.Failure<StartedHoldOutRun>(HoldOutRegistrationErrors.Drift("The block's end changed since registration."));
        }

        if (await inputs.DriftAsync(config, cancellationToken) is { } drift)
        {
            return Result.Failure<StartedHoldOutRun>(HoldOutRegistrationErrors.Drift("A stored input no longer resolves: " + drift + "."));
        }

        // the horizon (HO-01 D5): the registration must be frozen before the block begins, and block 1 never takes a system look; the slot must be free. Either refusal stores nothing and reads nothing.
        if (!HoldOutHorizon.IsPredated(HoldOutHorizon.SystemKind, block.PrevStart is null, block.Start, registration.CreatedAt, null))
        {
            return Result.Failure<StartedHoldOutRun>(Error.Conflict("replay.holdout_not_predated", "The registration was stored on or after the start of the block (or the block is block 1), so it cannot take a fresh system look."));
        }

        if (await blocks.HasFreshLookAsync(block.Start, HoldOutHorizon.SystemKind, cancellationToken))
        {
            return Result.Failure<StartedHoldOutRun>(HoldOutRegistrationErrors.Used("The system look of this block is taken by another registration."));
        }

        var now = time.GetUtcNow();
        var view = new HoldOutResultView(
            key, HoldOutRegistrationIds.LookVersion, HoldOutHorizon.SystemKind, command.Mode, "used", now, block.Start, block.HistoryLast, false, false, false, HoldOutStatus.Inconclusive,
            "The system look is claimed; the verdict is stored with the registration (GET /api/replay/holdout-registrations).", null, null, null, 0, registration.ConfigHash, options.SnapshotHash(), command.Actor,
            command.SwitchStamp is { } stamp ? ["Research only: unseen by the system; the owner may have seen these prices elsewhere.", "Allowed by the auto_run switch row " + stamp + "."] : ["Research only: unseen by the system; the owner may have seen these prices elsewhere."]);
        var look = new StoredUnlock(key, HoldOutRegistrationIds.LookVersion, HoldOutHorizon.SystemKind, command.Mode, command.Actor, now, options.SnapshotHash(), false, block.Start, HoldOutRegistrationIds.LookId(registration.Id), view);
        if (!await blocks.InsertUnlockAsync(look, cancellationToken))
        {
            return Result.Failure<StartedHoldOutRun>(HoldOutRegistrationErrors.Used("The system look could not be claimed: the slot was taken, or the block was released, between the check and the claim."));
        }

        // one trial-ledger look of kind system_replay per strategy version, keyed by the look; the runner appends the same rows again before it reads anything (the look and the ledger are separate connections), and a second append writes nothing
        await HoldOutLedgerRows.AppendAsync(ledger, registration.Id, config, now, cancellationToken);

        return Result.Success(new StartedHoldOutRun(runIds));
    }
}
