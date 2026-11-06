using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Confidence;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Confidence;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>
/// Records a cell move (spec P6-08, ADR 0073): the lifecycle table decides, the changelog row is the only record. The state read is checked again under the subject's lock when the row is appended. A move the table allows from Candidate to Paper or from Paper to Active is also
/// judged by the CF-11 readiness gate (R or S) through <see cref="IReadinessGate"/>, the only route to the scorecard: with the owner's enforce switch OFF (the default) a failed gate only adds the gate's snapshot and failing rows to the evidence of the row; ON, it refuses the move.
/// </summary>
internal sealed class RecordCellTransitionHandler(ILearningChangelog changelog, IReadinessGate gate, TimeProvider time, ILogger<RecordCellTransitionHandler> logger) : ICommandHandler<RecordCellTransitionCommand, ChangelogOutcome>
{
    private const int MaxEvidence = 20;
    private const int MaxReason = 400; // the stored reason also carries the claimed facts, within the column's 500

    /// <summary>The facts are claims by the caller and nobody checks them; the row shows what was claimed.</summary>
    private static string ClaimedPrefix(TransitionFacts f) =>
        $"[claimed, unverified: gates={Yn(f.GatesPassed)} owner={Yn(f.OwnerApproved)} band={Yn(f.WithinBand)} recovered={Yn(f.Recovered)} paperDays={f.PaperDays}] ";

    private static string Yn(bool value) => value ? "yes" : "no";

    public async ValueTask<Result<ChangelogOutcome>> HandleAsync(RecordCellTransitionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var problems = Validate(command);
        if (problems.Count > 0)
        {
            ChangelogLog.Refused(logger, LearningErrors.InvalidCode);
            return Result.Failure<ChangelogOutcome>(LearningErrors.Invalid(problems));
        }

        var history = await changelog.HistoryAsync(command.Cell, cancellationToken);
        var replay = CellHistory.Replay(history);
        if (replay.Problems.Count > 0)
        {
            ChangelogLog.Refused(logger, ChangelogErrors.InconsistentCode);
            return Result.Failure<ChangelogOutcome>(ChangelogErrors.Inconsistent(command.Cell, replay.Problems));
        }

        var refusals = new List<string>(CellLifecycle.Refusals(replay.State, command.To, command.Facts));
        if (replay.State == CellState.Paused && !command.OwnerReEnable && CellHistory.PauseOrigin(history) is { } origin && KillEvidence.NamesProposal(origin.Entry.EvidenceIds))
        {
            refusals.Add("This pause was made by a kill proposal: it is lifted only by the owner's re-enable act (POST /api/learning/cells/{key}/re-enable, with a fresh password and the failing condition cleared), not by a lifecycle move.");
        }

        if (refusals.Count == 0 && CellLifecycle.NeedsEvidence(replay.State, command.To) && command.EvidenceIds.Count == 0)
        {
            refusals.Add("This move needs at least one evidence id.");
        }

        if (refusals.Count > 0)
        {
            ChangelogLog.Refused(logger, ChangelogErrors.TransitionRefusedCode);
            return Result.Failure<ChangelogOutcome>(ChangelogErrors.TransitionRefused(command.Cell, refusals));
        }

        // CF-11: the readiness gate of the move, called only for a move the table allowed (a refused or illegal move leaves no gate snapshot)
        IReadOnlyList<string> evidence = command.EvidenceIds;
        if (ReadinessGateKinds.For(replay.State, command.To) is { } kind)
        {
            // the owner's evidence is never cut to make room for the gate's: a gated move carries at most the ids that leave room for them, and is refused (422) before the gate is asked otherwise
            if (command.EvidenceIds.Count > MaxEvidence - ReadinessGateKinds.MaxGateEvidenceIds)
            {
                ChangelogLog.Refused(logger, LearningErrors.InvalidCode);
                return Result.Failure<ChangelogOutcome>(LearningErrors.Invalid([new FieldError("evidence", $"A move that a readiness gate judges carries at most {MaxEvidence - ReadinessGateKinds.MaxGateEvidenceIds} evidence ids of its own: the gate adds up to {ReadinessGateKinds.MaxGateEvidenceIds} (its snapshot and its result).")]));
            }

            var decision = await gate.CheckAsync(new ReadinessGateRequest(command.Cell, kind, command.Actor), cancellationToken);
            if (decision.Refused)
            {
                ChangelogLog.Refused(logger, ReadinessErrors.GateFailedCode);
                return Result.Failure<ChangelogOutcome>(ReadinessErrors.GateFailed(decision.Gate, decision.Message));
            }

            evidence = [.. command.EvidenceIds, .. decision.EvidenceIds.Take(ReadinessGateKinds.MaxGateEvidenceIds)];
        }

        var paperDays = replay.State == CellState.Paper && command.To == CellState.Active ? command.Facts.PaperDays : (int?)null;
        var entry = new NewChangelogEntry("transition", "cell", command.Cell, replay.State.Code(), command.To.Code(), ClaimedPrefix(command.Facts) + command.Reason.Trim(), command.Actor, evidence, command.BeforeRef, command.AfterRef, paperDays, null, time.GetUtcNow());
        var appended = await changelog.AppendAsync(entry, cancellationToken);
        if (appended.Stale)
        {
            ChangelogLog.Refused(logger, ChangelogErrors.StaleCode);
            return Result.Failure<ChangelogOutcome>(ChangelogErrors.Stale(command.Cell));
        }

        ChangelogLog.Transition(logger, appended.Id, entry.FromState, entry.ToState);
        return Result.Success(new ChangelogOutcome(appended.Id, command.Cell, entry.FromState, entry.ToState));
    }

    private static List<FieldError> Validate(RecordCellTransitionCommand command)
    {
        var problems = new List<FieldError>();
        if (CellId.TryParse(command.Cell) is null)
        {
            problems.Add(new FieldError("cell", "The cell is strategy|timeframe|side|style|exit|regime, or those six parts then | and a universe (*) and an expression."));
        }

        if (!Enum.IsDefined(command.To))
        {
            problems.Add(new FieldError("to", "The state is research, candidate, paper, active, paused or retired."));
        }

        if (string.IsNullOrWhiteSpace(command.Reason) || command.Reason.Length > MaxReason)
        {
            problems.Add(new FieldError("reason", $"The reason is required and at most {MaxReason} characters."));
        }

        if (string.IsNullOrWhiteSpace(command.Actor) || command.Actor.Length > 100)
        {
            problems.Add(new FieldError("actor", "The actor is required and at most 100 characters."));
        }

        if (command.Facts is null || command.Facts.PaperDays is < 0 or > 100_000)
        {
            problems.Add(new FieldError("paperDays", "The paper days are 0 or more."));
        }

        if (command.EvidenceIds is null || command.EvidenceIds.Count > MaxEvidence || command.EvidenceIds.Any(e => string.IsNullOrWhiteSpace(e) || e.Length > 100))
        {
            problems.Add(new FieldError("evidence", $"At most {MaxEvidence} evidence ids of 1 to 100 characters."));
        }

        if (command.BeforeRef?.Length > 200 || command.AfterRef?.Length > 200)
        {
            problems.Add(new FieldError("refs", "A reference is at most 200 characters."));
        }

        return problems;
    }
}
