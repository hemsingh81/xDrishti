using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>Reverts the latest transition of a cell with a new row that points at it (spec P6-08, ADR 0073). Never an update or a delete; a revert cannot be reverted (record the move again), designations are not reverted here (designate again), and a revert never makes a cell active.</summary>
internal sealed class RevertChangelogHandler(ILearningChangelog changelog, TimeProvider time, ILogger<RevertChangelogHandler> logger) : ICommandHandler<RevertChangelogCommand, ChangelogOutcome>
{
    public async ValueTask<Result<ChangelogOutcome>> HandleAsync(RevertChangelogCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var problems = new List<FieldError>();
        if (string.IsNullOrWhiteSpace(command.Reason) || command.Reason.Length > 500)
        {
            problems.Add(new FieldError("reason", "The reason is required and at most 500 characters."));
        }

        if (string.IsNullOrWhiteSpace(command.Actor) || command.Actor.Length > 100)
        {
            problems.Add(new FieldError("actor", "The actor is required and at most 100 characters."));
        }

        if (problems.Count > 0)
        {
            ChangelogLog.Refused(logger, LearningErrors.InvalidCode);
            return Result.Failure<ChangelogOutcome>(LearningErrors.Invalid(problems));
        }

        var target = await changelog.FindAsync(command.Id, cancellationToken);
        if (target is null)
        {
            ChangelogLog.Refused(logger, ChangelogErrors.NotFoundCode);
            return Result.Failure<ChangelogOutcome>(ChangelogErrors.NotFound(command.Id));
        }

        var e = target.Entry;
        var reasons = new List<string>();
        var restored = CellStateExtensions.Parse(e.FromState);
        if (e.Kind != "transition" || restored is null)
        {
            reasons.Add("Only a cell transition can be reverted; a revert is not reverted (record the move again) and a designation is changed by designating again.");
        }
        else
        {
            reasons.AddRange(CellLifecycle.RevertRefusals(restored.Value));
            var history = await changelog.HistoryAsync(e.Subject, cancellationToken);
            if (history.Count == 0 || history[^1].Id != target.Id)
            {
                reasons.Add("It is not the latest row of the cell, or it was reverted already.");
            }
        }

        if (reasons.Count > 0)
        {
            ChangelogLog.Refused(logger, ChangelogErrors.NotRevertibleCode);
            return Result.Failure<ChangelogOutcome>(ChangelogErrors.NotRevertible(command.Id, reasons));
        }

        var entry = new NewChangelogEntry("revert", "cell", e.Subject, e.ToState, e.FromState, command.Reason.Trim(), command.Actor, [$"changelog:{target.Id}"], e.AfterRef, e.BeforeRef, null, target.Id, time.GetUtcNow());
        var appended = await changelog.AppendAsync(entry, cancellationToken);
        if (appended.Stale)
        {
            ChangelogLog.Refused(logger, ChangelogErrors.StaleCode);
            return Result.Failure<ChangelogOutcome>(ChangelogErrors.Stale(e.Subject));
        }

        ChangelogLog.Reverted(logger, appended.Id, target.Id);
        return Result.Success(new ChangelogOutcome(appended.Id, e.Subject, entry.FromState, entry.ToState));
    }
}
