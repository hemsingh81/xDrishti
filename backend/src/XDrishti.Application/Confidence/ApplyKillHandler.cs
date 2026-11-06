using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Confidence;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Learning;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Confidence;

/// <summary>
/// The execution of a kill proposal (spec CF-12 D2 and D3, part 12b, ADR 0219): demote first, then compare-and-set the proposal. Every cell the proposal names that is still Paper or Active is paused through <see cref="PauseCellCommand"/> (the lifecycle, a changelog row
/// with the rule, the numbers and the settings hash); a cell already paused is skipped, so a repeat or a concurrent call changes nothing twice. The proposal is then moved from <c>proposed</c> to <c>applied</c> in one conditional update, so only one call records the decision.
/// An <c>owner</c> actor is the signed-in owner. The <c>auto</c> actor needs the stored setting ON and a proposal that is not proposal-only (re-checked here, and refused by a database constraint as well); it is recorded as <c>auto</c> with the id of the setting row in force,
/// never as an owner. The only lifecycle move this handler can ask for is the pause. It re-enables nothing and places no order.
/// </summary>
internal sealed class ApplyKillHandler(
    IKillProposalReader reader,
    IKillProposalStore store,
    IKillSettings settings,
    StrategyCells cells,
    ICommandHandler<PauseCellCommand, ChangelogOutcome> pause,
    ILogger<ApplyKillHandler> logger) : ICommandHandler<ApplyKillCommand, ApplyKillOutcome>
{
    private const int MaxReason = 400; // the changelog reason of the lifecycle handler

    public async ValueTask<Result<ApplyKillOutcome>> HandleAsync(ApplyKillCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var auto = command.Actor == KillActors.Auto;
        if (!auto && !KillActors.IsOwner(command.Actor))
        {
            KillLog.Refused(logger, KillErrors.OwnerOnlyCode);
            return Result.Failure<ApplyKillOutcome>(KillErrors.OwnerOnly());
        }

        var row = await reader.FindAsync(command.ProposalId, cancellationToken);
        if (row is null)
        {
            return Result.Failure<ApplyKillOutcome>(KillErrors.ProposalNotFound(command.ProposalId));
        }

        if (row.State == "applied")
        {
            return Result.Success(Outcome(row, true, [], [], row.Actor));
        }

        if (row.State != "proposed")
        {
            KillLog.Refused(logger, KillErrors.ProposalClosedCode);
            return Result.Failure<ApplyKillOutcome>(KillErrors.ProposalClosed(row.Id));
        }

        var current = await settings.CurrentAsync(cancellationToken);
        if (auto)
        {
            if (!current.Auto)
            {
                KillLog.Refused(logger, KillErrors.AutoOffCode);
                return Result.Failure<ApplyKillOutcome>(KillErrors.AutoOff());
            }

            if (!KillDecision.MayExecuteAutomatically(current.Auto, row.Proposal.ProposalOnly))
            {
                KillLog.Refused(logger, KillErrors.ProposalOnlyCode);
                return Result.Failure<ApplyKillOutcome>(KillErrors.ProposalOnly());
            }
        }

        var p = row.Proposal;
        var meta = $" | proposal {row.Id}; settings {p.SettingsHash[..12]}" + (auto ? $"; automatic, switch row {current.Id}" : string.Empty);
        var reason = $"kill rule {p.Rule}: {p.Text}";
        reason = (reason.Length > MaxReason - meta.Length ? reason[..(MaxReason - meta.Length)] : reason) + meta;
        var evidence = new[] { KillEvidence.ForProposal(row.Id) };
        var paused = new List<string>();
        var skipped = new List<string>();
        foreach (var cell in p.Cells)
        {
            if (await cells.StateAsync(cell, cancellationToken) is not (CellState.Paper or CellState.Active))
            {
                skipped.Add(cell);
                continue;
            }

            var moved = await pause.HandleAsync(new PauseCellCommand(cell, reason, command.Actor, evidence, auto ? $"kill-switch:{current.Id}" : null, "settings:" + p.SettingsHash[..16]), cancellationToken);
            if (moved.IsSuccess)
            {
                paused.Add(cell);
            }
            else if (await cells.StateAsync(cell, cancellationToken) == CellState.Paused)
            {
                skipped.Add(cell); // a concurrent call paused it first
            }
            else
            {
                KillLog.Refused(logger, moved.Error.Code);
                return Result.Failure<ApplyKillOutcome>(moved.Error); // nothing is marked applied: a retry pauses the rest
            }
        }

        // shortcut: if an automatic and a manual apply race, the changelog rows record who paused each cell (each is true) and the proposal records who won this update, so the two can differ; one writer per proposal would need a lock across the changelog and this table.
        var result = await store.DecideAsync(row.Id, "applied", command.Actor, auto ? current.Id : null, cancellationToken);
        if (result == KillDecideResult.Refused)
        {
            // The record refuses an automatic decision when the switch is not ON now (it was turned off after the check above) or the rule is proposal-only. The guard is on the proposal record only: the cells above were already paused through the changelog, which the database cannot tie to the switch.
            KillLog.Refused(logger, KillErrors.DecisionRefusedCode);
            return Result.Failure<ApplyKillOutcome>(KillErrors.DecisionRefused(row.Id));
        }

        if (result == KillDecideResult.NotProposed)
        {
            var decided = await reader.FindAsync(row.Id, cancellationToken) ?? row;
            return Result.Success(Outcome(decided, true, paused, skipped, decided.Actor));
        }

        KillLog.Applied(logger, row.Id, auto ? "auto" : "owner", paused.Count, skipped.Count);
        return Result.Success(Outcome(row, false, paused, skipped, command.Actor));
    }

    private static ApplyKillOutcome Outcome(KillProposalRow row, bool already, IReadOnlyList<string> paused, IReadOnlyList<string> skipped, string? actor) =>
        new(row.Id, row.Proposal.Strategy, row.Proposal.Rule, "applied", already, paused, skipped, actor);
}
