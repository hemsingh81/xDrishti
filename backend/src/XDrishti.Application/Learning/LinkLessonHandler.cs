using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>Links a hypothesis or an experiment to an open lesson (spec P6-16). The fix must exist; linking is the owner's and applies nothing.</summary>
internal sealed class LinkLessonHandler(ILessonLog log, IHypothesisLog hypotheses, IExperimentLedger ledger, TimeProvider time, ILogger<LinkLessonHandler> logger) : ICommandHandler<LinkLessonCommand, LessonMoveOutcome>
{
    public async ValueTask<Result<LessonMoveOutcome>> HandleAsync(LinkLessonCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var problems = LessonRows.CheckReasonAndActor(command.Reason, command.Actor);
        if (command.FixKind is not ("hypothesis" or "experiment") || command.FixId < 1)
        {
            problems.Add(new FieldError("fix", "Name a hypothesis or an experiment and its id (1 or more)."));
        }

        if (problems.Count > 0)
        {
            return Refuse(LearningErrors.Invalid(problems));
        }

        var latest = await log.FindAsync(command.Id, cancellationToken);
        if (latest is null)
        {
            return Refuse(LessonErrors.NotFound(command.Id));
        }

        var from = LessonStateText.TryParse(latest.Row.State) ?? LessonState.Dismissed;
        var refusals = LessonLifecycle.Refusals(from, LessonState.Linked, new LessonFacts(HypothesisRows.IsOwner(command.Actor)));
        if (refusals.Count > 0)
        {
            return Refuse(LessonErrors.MoveRefused(refusals));
        }

        var isHypothesis = command.FixKind == "hypothesis";
        var exists = isHypothesis
            ? await hypotheses.FindAsync(command.FixId, cancellationToken) is not null
            : await ledger.FindAsync(command.FixId, cancellationToken) is not null;
        if (!exists)
        {
            return Refuse(LessonErrors.FixNotFound(command.FixKind, command.FixId));
        }

        var next = LessonRows.Next(latest, LessonState.Linked, command.Reason, command.Actor, time.GetUtcNow()) with
        {
            HypothesisRef = isHypothesis ? command.FixId : null,
            ExperimentId = isHypothesis ? null : command.FixId,
        };
        var appended = await log.AppendAsync(next, cancellationToken);
        if (appended.Status != LessonAppendStatus.Created)
        {
            return Refuse(LessonErrors.Stale());
        }

        var fromCode = from.Code();
        var toCode = LessonState.Linked.Code();
        LessonLog.Moved(logger, command.Id, fromCode, toCode);
        return Result.Success(new LessonMoveOutcome(command.Id, LessonState.Linked.Code(), $"Linked to {command.FixKind} {command.FixId}. Nothing was changed; applying a fix is your own act."));
    }

    private Result<LessonMoveOutcome> Refuse(Error error)
    {
        LessonLog.Refused(logger, error.Code);
        return Result.Failure<LessonMoveOutcome>(error);
    }
}
