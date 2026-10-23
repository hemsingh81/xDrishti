using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>
/// Running to Judged (spec P6-10): runs the P6-11 experiment between the stored baseline and candidate runs, which records it and counts the trial, then writes the Judged row with the experiment id. When the experiment is refused the hypothesis stays Running
/// (nothing is recorded, as for any refused experiment); when the same experiment was recorded already, that experiment is linked and counted once.
/// </summary>
internal sealed class JudgeHypothesisHandler(IHypothesisLog log, ICommandHandler<RunExperimentCommand, RunExperimentOutcome> experiments, TimeProvider time, ILogger<JudgeHypothesisHandler> logger) : ICommandHandler<JudgeHypothesisCommand, HypothesisMoveOutcome>
{
    public async ValueTask<Result<HypothesisMoveOutcome>> HandleAsync(JudgeHypothesisCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Options is null || !HypothesisRows.ValidActor(command.Actor))
        {
            return Refuse(LearningErrors.Invalid([new FieldError("options", "The options and the actor are required.")]));
        }

        var latest = await log.FindAsync(command.Id, cancellationToken);
        if (latest is null)
        {
            return Refuse(HypothesisErrors.NotFound(command.Id));
        }

        var row = latest.Row;
        var from = HypothesisStateText.TryParse(row.State) ?? HypothesisState.Expired;
        var refusals = HypothesisLifecycle.Refusals(from, HypothesisState.Judged, new HypothesisFacts(HypothesisRows.IsOwner(command.Actor), AutonomyLevel.Supervised));
        if (refusals.Count > 0 || row.CandidateRunKey is null)
        {
            return Refuse(HypothesisErrors.MoveRefused(refusals.Count > 0 ? refusals : ["No candidate run is attached."]));
        }

        var knob = new ExperimentKnob(row.KnobName, Enum.Parse<ExperimentKnobKind>(row.KnobKind), row.BaselineValue, row.CandidateValue);
        var judged = await experiments.HandleAsync(new RunExperimentCommand(row.BaselineRunKey, row.CandidateRunKey, knob, command.Options, command.HoldOutMonths, command.Actor), cancellationToken);
        if (judged.IsFailure)
        {
            return Refuse(judged.Error);
        }

        var e = judged.Value;
        var reason = $"experiment {e.Id}: {e.Outcome}" + (e.Reasons.Count > 0 ? "; " + e.Reasons[0] : string.Empty);
        var appended = await log.AppendAsync(HypothesisRows.Next(latest, HypothesisState.Judged, reason, command.Actor, time.GetUtcNow(), experimentId: e.Id), cancellationToken);
        if (appended.Status != HypothesisAppendStatus.Created)
        {
            return Refuse(HypothesisErrors.Stale());
        }

        HypothesisLog.Judged(logger, command.Id, e.Id, e.Outcome);
        return Result.Success(new HypothesisMoveOutcome(command.Id, HypothesisState.Judged.Code(), $"Judged by experiment {e.Id}: {e.Outcome} ({e.Trials} trials in 30 days). Accept or reject it yourself; nothing is applied.\n{e.ReportText}"));
    }

    private Result<HypothesisMoveOutcome> Refuse(Error error)
    {
        HypothesisLog.Refused(logger, error.Code);
        return Result.Failure<HypothesisMoveOutcome>(error);
    }
}
