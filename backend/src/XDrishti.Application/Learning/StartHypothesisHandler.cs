using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>Approved to Running (spec P6-10): the one-variable rule is checked against the two stored run definitions and a pair that differs in more, or not in the knob, is refused with the state unchanged.</summary>
internal sealed class StartHypothesisHandler(IRunStore runs, IHypothesisLog log, TimeProvider time, ILogger<StartHypothesisHandler> logger) : ICommandHandler<StartHypothesisCommand, HypothesisMoveOutcome>
{
    public async ValueTask<Result<HypothesisMoveOutcome>> HandleAsync(StartHypothesisCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!RunRecordText.IsHex64(command.CandidateRunKey) || !HypothesisRows.ValidActor(command.Actor))
        {
            return Refuse(LearningErrors.Invalid([new FieldError("candidateRunKey", "A run key is 64 lower-case hex characters, and the actor is required.")]));
        }

        var latest = await log.FindAsync(command.Id, cancellationToken);
        if (latest is null)
        {
            return Refuse(HypothesisErrors.NotFound(command.Id));
        }

        var row = latest.Row;
        var from = HypothesisStateText.TryParse(row.State) ?? HypothesisState.Expired;
        var refusals = HypothesisLifecycle.Refusals(from, HypothesisState.Running, new HypothesisFacts(HypothesisRows.IsOwner(command.Actor), AutonomyLevel.Supervised));
        if (refusals.Count > 0)
        {
            return Refuse(HypothesisErrors.MoveRefused(refusals));
        }

        if (string.Equals(row.BaselineRunKey, command.CandidateRunKey, StringComparison.Ordinal))
        {
            return Refuse(HypothesisErrors.NotOneVariable(["the candidate is the baseline run"]));
        }

        var baseline = await ReadAsync(row.BaselineRunKey, cancellationToken);
        var candidate = await ReadAsync(command.CandidateRunKey, cancellationToken);
        if (baseline.IsFailure || candidate.IsFailure)
        {
            return Refuse(baseline.IsFailure ? baseline.Error : candidate.Error);
        }

        var knob = new ExperimentKnob(row.KnobName, Enum.Parse<ExperimentKnobKind>(row.KnobKind), row.BaselineValue, row.CandidateValue);
        var problems = OneVariableRule.Problems(knob, baseline.Value, candidate.Value);
        if (problems.Count > 0)
        {
            return Refuse(HypothesisErrors.NotOneVariable(problems));
        }

        var appended = await log.AppendAsync(HypothesisRows.Next(latest, HypothesisState.Running, "candidate run attached; it differs from the baseline in the one knob", command.Actor, time.GetUtcNow(), command.CandidateRunKey), cancellationToken);
        if (appended.Status != HypothesisAppendStatus.Created)
        {
            return Refuse(HypothesisErrors.Stale());
        }

        var fromCode = from.Code();
        HypothesisLog.Moved(logger, command.Id, fromCode, "running");
        return Result.Success(new HypothesisMoveOutcome(command.Id, HypothesisState.Running.Code(), "Running: the candidate differs from the baseline in the one knob. Judge it next; that records an experiment and counts as a trial."));
    }

    private async ValueTask<Result<RunDefinition>> ReadAsync(string runKey, CancellationToken cancellationToken)
    {
        var header = await runs.FindAsync(runKey, cancellationToken);
        var record = header is null ? null : await runs.ReadRecordTextAsync(runKey, cancellationToken);
        if (header is null || record is null)
        {
            return Result.Failure<RunDefinition>(LearningErrors.RunNotFound(runKey));
        }

        var definition = RunDefinition.TryRead(record, header.Fills, out var problem);
        return definition is null
            ? Result.Failure<RunDefinition>(LearningErrors.Invalid([new FieldError("runKey", problem!)]))
            : Result.Success(definition);
    }

    private Result<HypothesisMoveOutcome> Refuse(Error error)
    {
        HypothesisLog.Refused(logger, error.Code);
        return Result.Failure<HypothesisMoveOutcome>(error);
    }
}
