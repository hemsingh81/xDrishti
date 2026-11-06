using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>
/// The execution of a rollback proposal (spec CF-10 D8, part 10d-1, ADR 0220): demote first, then compare-and-set the proposal. The champion moves to challenger through <see cref="DemoteModelCommand"/> (the registry's designation and a changelog row with the trigger, the numbers, the
/// proposal and, for <c>auto</c>, the switch row and the settings hash); a model that is not the champion any more is skipped, so a repeat or a concurrent call changes nothing twice. The proposal is then moved from <c>proposed</c> to <c>applied</c> in one conditional update.
/// An <c>owner</c> actor is the signed-in owner (no password: the safe direction). The <c>auto</c> actor needs <c>ml.auto_rollback</c> ON (re-checked here, and refused by a database trigger as well) and is recorded as <c>auto</c> with the id of the setting row in force, never as an
/// owner. This handler names no promotion and no designation but the demotion: the previous champion is reported, not re-promoted.
/// </summary>
internal sealed class ApplyRollbackHandler(
    IRollbackProposalReader reader,
    IRollbackProposalStore store,
    IMlSwitchSettings switches,
    IModelRegistry registry,
    ICommandHandler<DemoteModelCommand, DesignationOutcome> demote,
    ILogger<ApplyRollbackHandler> logger) : ICommandHandler<ApplyRollbackCommand, ApplyRollbackOutcome>
{
    private const int MaxReason = 500; // the changelog reason

    public async ValueTask<Result<ApplyRollbackOutcome>> HandleAsync(ApplyRollbackCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var auto = command.Actor == MlActors.Auto;
        if (!auto && !MlActors.IsOwner(command.Actor))
        {
            MlSafetyLog.Refused(logger, MlSafetyErrors.OwnerOnlyCode);
            return Result.Failure<ApplyRollbackOutcome>(MlSafetyErrors.OwnerOnly());
        }

        var row = await reader.FindAsync(command.ProposalId, cancellationToken);
        if (row is null)
        {
            return Result.Failure<ApplyRollbackOutcome>(MlSafetyErrors.RollbackNotFound(command.ProposalId));
        }

        if (row.State == "applied")
        {
            return Result.Success(Outcome(row, true, false, row.Actor));
        }

        if (row.State != "proposed")
        {
            MlSafetyLog.Refused(logger, MlSafetyErrors.RollbackClosedCode);
            return Result.Failure<ApplyRollbackOutcome>(MlSafetyErrors.RollbackClosed(row.Id));
        }

        var current = await switches.CurrentAsync(cancellationToken);
        if (auto && !current.AutoRollback.On)
        {
            MlSafetyLog.Refused(logger, MlSafetyErrors.RollbackAutoOffCode);
            return Result.Failure<ApplyRollbackOutcome>(MlSafetyErrors.RollbackAutoOff());
        }

        var p = row.Proposal;
        var demoted = false;
        var model = await registry.FindAsync(p.ModelId, cancellationToken);
        if (model is { Role: ModelRole.Champion })
        {
            var meta = $" | rollback proposal {row.Id}; settings {p.SettingsHash[..12]}" + (auto ? $"; automatic, switch row {current.AutoRollback.Id}" : string.Empty);
            var reason = $"rollback ({p.Trigger}): {p.Text}";
            reason = (reason.Length > MaxReason - meta.Length ? reason[..(MaxReason - meta.Length)] : reason) + meta;
            string[] evidence = auto ? [$"rollback-proposal:{row.Id}", $"ml-switch:{current.AutoRollback.Id}", "settings:" + p.SettingsHash[..16]] : [$"rollback-proposal:{row.Id}", "settings:" + p.SettingsHash[..16]];
            var moved = await demote.HandleAsync(new DemoteModelCommand(p.ModelId, reason, command.Actor, evidence), cancellationToken);
            if (moved.IsFailure)
            {
                MlSafetyLog.Refused(logger, moved.Error.Code);
                return Result.Failure<ApplyRollbackOutcome>(moved.Error); // nothing is marked applied: a retry repeats the demotion
            }

            demoted = moved.Value.Changed;
        }

        // shortcut: if an automatic and a manual apply race, the changelog row records who demoted the model (it is true) and the proposal records who won this update, so the two can differ; one writer per proposal would need a lock across the registry and this table.
        var result = await store.DecideAsync(row.Id, "applied", command.Actor, auto ? current.AutoRollback.Id : null, cancellationToken);
        if (result == RollbackDecideResult.Refused)
        {
            // the record refuses an automatic decision when the switch is not ON now (it was turned off after the check above); the champion may already be demoted, which is the safe direction
            MlSafetyLog.Refused(logger, MlSafetyErrors.RollbackDecisionRefusedCode);
            return Result.Failure<ApplyRollbackOutcome>(MlSafetyErrors.RollbackDecisionRefused(row.Id));
        }

        if (result == RollbackDecideResult.NotProposed)
        {
            var decided = await reader.FindAsync(row.Id, cancellationToken) ?? row;
            return Result.Success(Outcome(decided, true, demoted, decided.Actor));
        }

        MlSafetyLog.RollbackApplied(logger, row.Id, auto ? "auto" : "owner", demoted);
        return Result.Success(Outcome(row, false, demoted, command.Actor));
    }

    private static ApplyRollbackOutcome Outcome(RollbackProposalRow row, bool already, bool demoted, string? actor) =>
        new(row.Id, row.Proposal.ModelId, row.Proposal.Trigger, "applied", already, demoted, row.Proposal.PreviousChampionId, row.Proposal.PreviousQualifies, actor);
}
