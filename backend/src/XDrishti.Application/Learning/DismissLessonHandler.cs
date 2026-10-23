using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>Sets an open or linked lesson aside (spec P6-16). The owner's act; a new row, the lesson is kept.</summary>
internal sealed class DismissLessonHandler(ILessonLog log, TimeProvider time, ILogger<DismissLessonHandler> logger) : ICommandHandler<DismissLessonCommand, LessonMoveOutcome>
{
    public async ValueTask<Result<LessonMoveOutcome>> HandleAsync(DismissLessonCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var problems = LessonRows.CheckReasonAndActor(command.Reason, command.Actor);
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
        var refusals = LessonLifecycle.Refusals(from, LessonState.Dismissed, new LessonFacts(HypothesisRows.IsOwner(command.Actor)));
        if (refusals.Count > 0)
        {
            return Refuse(LessonErrors.MoveRefused(refusals));
        }

        var appended = await log.AppendAsync(LessonRows.Next(latest, LessonState.Dismissed, command.Reason, command.Actor, time.GetUtcNow()), cancellationToken);
        if (appended.Status != LessonAppendStatus.Created)
        {
            return Refuse(LessonErrors.Stale());
        }

        var fromCode = from.Code();
        var toCode = LessonState.Dismissed.Code();
        LessonLog.Moved(logger, command.Id, fromCode, toCode);
        return Result.Success(new LessonMoveOutcome(command.Id, LessonState.Dismissed.Code(), "Dismissed."));
    }

    private Result<LessonMoveOutcome> Refuse(Error error)
    {
        LessonLog.Refused(logger, error.Code);
        return Result.Failure<LessonMoveOutcome>(error);
    }
}
