using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>
/// Closes a linked lesson (spec P6-16, ADR 0078). The before and after numbers are read from the stored experiment of the linked fix (a linked hypothesis gives its judging experiment); the caller states none. The result is
/// <c>improved</c> only when that experiment was Better; every other outcome is "no measurable change", which is a legitimate result. Applies nothing.
/// </summary>
internal sealed class CloseLessonHandler(ILessonLog log, IHypothesisLog hypotheses, IExperimentLedger ledger, TimeProvider time, ILogger<CloseLessonHandler> logger) : ICommandHandler<CloseLessonCommand, LessonMoveOutcome>
{
    public const string Improved = "improved";

    public const string NoChange = "no_measurable_change";

    public async ValueTask<Result<LessonMoveOutcome>> HandleAsync(CloseLessonCommand command, CancellationToken cancellationToken)
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

        var row = latest.Row;
        var experimentId = row.ExperimentId;
        if (experimentId is null && row.HypothesisRef is { } hypothesisId)
        {
            experimentId = (await hypotheses.FindAsync(hypothesisId, cancellationToken))?.Row.ExperimentId;
        }

        var stored = experimentId is { } id ? await ledger.FindAsync(id, cancellationToken) : null;
        var from = LessonStateText.TryParse(row.State) ?? LessonState.Dismissed;
        var facts = new LessonFacts(HypothesisRows.IsOwner(command.Actor), row.HypothesisRef is not null || row.ExperimentId is not null, stored is not null);
        var refusals = LessonLifecycle.Refusals(from, LessonState.Closed, facts);
        if (refusals.Count > 0)
        {
            return Refuse(LessonErrors.MoveRefused(refusals));
        }

        var e = stored!.Experiment;
        var result = e.Outcome == ExperimentOutcome.Better ? Improved : NoChange;
        var next = LessonRows.Next(latest, LessonState.Closed, command.Reason, command.Actor, time.GetUtcNow()) with
        {
            ExperimentId = stored.Id,
            Result = result,
            ExperimentOutcome = e.Outcome.ToString(),
            BeforeMeanR = e.PooledBaselineMeanR,
            AfterMeanR = e.PooledCandidateMeanR,
            BeforeTotalR = e.BaselineTotalR,
            AfterTotalR = e.CandidateTotalR,
        };
        var appended = await log.AppendAsync(next, cancellationToken);
        if (appended.Status != LessonAppendStatus.Created)
        {
            return Refuse(LessonErrors.Stale());
        }

        var fromCode = from.Code();
        var toCode = LessonState.Closed.Code();
        LessonLog.Moved(logger, command.Id, fromCode, toCode);
        LessonLog.Closed(logger, command.Id, stored.Id, result);
        var text = result == Improved
            ? $"Closed: improved (experiment {stored.Id} was Better). Nothing was changed; applying the fix is your own act."
            : $"Closed with no measurable change (experiment {stored.Id}: {e.Outcome}). That is a legitimate result.";
        return Result.Success(new LessonMoveOutcome(command.Id, LessonState.Closed.Code(), text));
    }

    private Result<LessonMoveOutcome> Refuse(Error error)
    {
        LessonLog.Refused(logger, error.Code);
        return Result.Failure<LessonMoveOutcome>(error);
    }
}
