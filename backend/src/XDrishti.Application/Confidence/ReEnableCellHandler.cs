using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Confidence;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Learning;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Confidence;

/// <summary>
/// The only way a kill-paused cell returns (spec CF-12 D3, part 12b, ADR 0219): <c>Paused</c> to <c>Paper</c>, by the owner, never to Active (that still needs the paper period). It refuses a non-owner actor and a reason that is missing before anything is read. The pause is
/// found in the changelog; when a kill proposal caused it, the strategy's kill rules are evaluated again and every rule that fired must now <b>hold</b> (a rule that cannot be evaluated is not a pass) and no rule may fire, else 409 <c>confidence.rule_not_cleared</c>. A pause no
/// kill rule made has no condition to clear, and says so. The CF-11 readiness gate R judges this move too (CF-12 D3), in the lifecycle recorder: with the owner's enforce switch ON a gate that does not pass refuses it (409 <c>confidence.gate_failed</c>); with it OFF the move goes on and the answer always carries a visible warning with the gate's own decision, never a silent pass. The move is recorded by the lifecycle recorder with the owner's facts. This handler
/// is not reachable from the automatic path (an architecture test).
/// </summary>
internal sealed class ReEnableCellHandler(
    ILearningChangelog changelog,
    IKillProposalReader proposals,
    IQueryHandler<GetDecayReportQuery, Result<DecayReportView>> report,
    ICommandHandler<RecordCellTransitionCommand, ChangelogOutcome> lifecycle,
    ILogger<ReEnableCellHandler> logger) : ICommandHandler<ReEnableCellCommand, ReEnableOutcome>
{
    /// <summary>The start of the first warning of every answer: the CF-11 readiness gate R judges this re-enable (CF-12 D3); the rest of the sentence is the gate's own decision.</summary>
    public const string GateWarning = "The CF-11 readiness gate R";

    /// <summary>The most evidence ids the re-enable sends: the lifecycle recorder leaves room for the two the gate adds.</summary>
    private const int MaxMoveEvidence = 20 - ReadinessGateKinds.MaxGateEvidenceIds;

    private const int MaxReason = 300;

    public async ValueTask<Result<ReEnableOutcome>> HandleAsync(ReEnableCellCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!KillActors.IsOwner(command.Actor))
        {
            KillLog.Refused(logger, KillErrors.OwnerOnlyCode);
            return Result.Failure<ReEnableOutcome>(KillErrors.OwnerOnly());
        }

        var reason = command.Reason?.Trim();
        if (string.IsNullOrEmpty(reason) || reason.Length > MaxReason)
        {
            return Result.Failure<ReEnableOutcome>(KillErrors.ReasonInvalid());
        }

        if (CellId.TryParse(command.Cell) is not { } cell)
        {
            return Result.Failure<ReEnableOutcome>(LearningErrors.Invalid([new FieldError("cell", "The cell is strategy|timeframe|side|style|exit|regime, or those six parts then | and a universe (*) and an expression.")]));
        }

        var history = await changelog.HistoryAsync(command.Cell, cancellationToken);
        var replay = CellHistory.Replay(history);
        if (replay.Problems.Count > 0)
        {
            return Result.Failure<ReEnableOutcome>(ChangelogErrors.Inconsistent(command.Cell, replay.Problems));
        }

        if (replay.State != CellState.Paused)
        {
            return Result.Failure<ReEnableOutcome>(KillErrors.NotPaused(command.Cell, replay.State));
        }

        // the pause the cell is in now, followed back through revert rows to the one that was first made; if its evidence names a kill proposal that cannot be read back, the cell stays paused (never fail open)
        var evidence = CellHistory.PauseOrigin(history)?.Entry.EvidenceIds ?? [];
        var named = KillEvidence.ProposalIds(evidence);
        var causes = new List<KillProposalRow>();
        foreach (var id in named)
        {
            if (await proposals.FindAsync(id, cancellationToken) is { } found)
            {
                causes.Add(found);
            }
        }

        if (KillEvidence.NamesProposal(evidence) && (named.Count == 0 || causes.Count != named.Count))
        {
            KillLog.Refused(logger, KillErrors.RuleNotClearedCode);
            return Result.Failure<ReEnableOutcome>(KillErrors.RuleNotCleared(command.Cell, "The kill proposal that paused it cannot be read back, so what has to clear is not known."));
        }

        var warnings = new List<string>();
        if (causes.Count == 0)
        {
            warnings.Add("The pause was not made by a kill proposal, so there was no kill condition to clear.");
        }
        else
        {
            var fired = causes.Select(c => c.Proposal.Rule).Distinct().ToList();
            var read = await report.HandleAsync(new GetDecayReportQuery(cell.Strategy), cancellationToken);
            if (read.IsFailure)
            {
                return Result.Failure<ReEnableOutcome>(KillErrors.RuleNotCleared(command.Cell, "The rules could not be evaluated now, and a rule that cannot be evaluated is not a pass."));
            }

            var now = read.Value.Kill.Select(k => (k.Rule, KillStates.Parse(k.State))).ToList();
            if (!KillDecision.IsCleared(fired, now))
            {
                var detail = string.Join("; ", fired.Select(rule => $"{rule} reads {read.Value.Kill.FirstOrDefault(k => k.Rule == rule)?.State ?? "not_evaluated"}")) + " (every rule that fired must hold now, and none may fire).";
                KillLog.Refused(logger, KillErrors.RuleNotClearedCode);
                return Result.Failure<ReEnableOutcome>(KillErrors.RuleNotCleared(command.Cell, detail));
            }
        }

        var moveEvidence = causes.Count == 0 ? ["re-enable:no-kill-proposal"] : causes.Select(c => "cleared-proposal:" + c.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)).Take(MaxMoveEvidence).ToList();
        var moved = await lifecycle.HandleAsync(new RecordCellTransitionCommand(command.Cell, CellState.Paper, "re-enabled by the owner: " + reason, command.Actor, new TransitionFacts(OwnerApproved: true, Recovered: true), moveEvidence) { OwnerReEnable = true }, cancellationToken);
        if (moved.IsFailure)
        {
            KillLog.Refused(logger, moved.Error.Code);
            return Result.Failure<ReEnableOutcome>(moved.Error);
        }

        KillLog.ReEnabled(logger);
        warnings.Insert(0, await GateNoteAsync(command.Cell, moved.Value.Id, cancellationToken));
        return Result.Success(new ReEnableOutcome(command.Cell, moved.Value.Id, moved.Value.From, moved.Value.To, warnings));
    }

    /// <summary>What the CF-11 gate R decided for this re-enable, read from the evidence ids it left on the row of the move (the gate's own record), never a silent pass.</summary>
    private async ValueTask<string> GateNoteAsync(string cell, long rowId, CancellationToken cancellationToken)
    {
        var row = (await changelog.HistoryAsync(cell, cancellationToken)).FirstOrDefault(r => r.Id == rowId);
        var result = row?.Entry.EvidenceIds.FirstOrDefault(e => e.StartsWith("readiness-gate-R:", StringComparison.Ordinal));
        if (result is null)
        {
            return GateWarning + " was asked about this re-enable, but its result is not on the row of the move.";
        }

        return result.StartsWith("readiness-gate-R:pass:", StringComparison.Ordinal)
            ? GateWarning + " passed for this re-enable; the snapshot it rests on is cited on the row."
            : GateWarning + " did NOT pass (" + result + "): the enforce switch is OFF (a failed gate refuses only when it is ON), so the re-enable went ahead; the failing rows and the snapshot are on the row.";
    }
}
