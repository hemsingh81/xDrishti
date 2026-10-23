using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>Records a cell move (spec P6-08, ADR 0073): the lifecycle table decides, the changelog row is the only record. The state read is checked again under the subject's lock when the row is appended.</summary>
internal sealed class RecordCellTransitionHandler(ILearningChangelog changelog, TimeProvider time, ILogger<RecordCellTransitionHandler> logger) : ICommandHandler<RecordCellTransitionCommand, ChangelogOutcome>
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
        if (refusals.Count == 0 && CellLifecycle.NeedsEvidence(replay.State, command.To) && command.EvidenceIds.Count == 0)
        {
            refusals.Add("This move needs at least one evidence id.");
        }

        if (refusals.Count > 0)
        {
            ChangelogLog.Refused(logger, ChangelogErrors.TransitionRefusedCode);
            return Result.Failure<ChangelogOutcome>(ChangelogErrors.TransitionRefused(command.Cell, refusals));
        }

        var paperDays = replay.State == CellState.Paper && command.To == CellState.Active ? command.Facts.PaperDays : (int?)null;
        var entry = new NewChangelogEntry("transition", "cell", command.Cell, replay.State.Code(), command.To.Code(), ClaimedPrefix(command.Facts) + command.Reason.Trim(), command.Actor, command.EvidenceIds, command.BeforeRef, command.AfterRef, paperDays, null, time.GetUtcNow());
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
            problems.Add(new FieldError("cell", "The cell is strategy|timeframe|side|style|exit|regime."));
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
