using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>Moves a hypothesis by the table of <see cref="HypothesisLifecycle"/> (spec P6-10). The move is a new row; accepting needs the owner and a Better experiment and applies nothing.</summary>
internal sealed class MoveHypothesisHandler(IHypothesisLog log, IExperimentLedger ledger, TimeProvider time, ILogger<MoveHypothesisHandler> logger) : ICommandHandler<MoveHypothesisCommand, HypothesisMoveOutcome>
{
    public async ValueTask<Result<HypothesisMoveOutcome>> HandleAsync(MoveHypothesisCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var problems = new List<FieldError>();
        if (!Enum.IsDefined(command.To) || command.To is HypothesisState.Proposed or HypothesisState.Running or HypothesisState.Judged)
        {
            problems.Add(new FieldError("to", "Use approved, rejected, expired or accepted here; running and judged have their own commands."));
        }

        if (string.IsNullOrWhiteSpace(command.Reason) || command.Reason.Length > HypothesisRows.MaxText)
        {
            problems.Add(new FieldError("reason", "The reason is required, at most 500 characters."));
        }

        if (!HypothesisRows.ValidActor(command.Actor))
        {
            problems.Add(new FieldError("actor", "The actor is required and at most 100 characters."));
        }

        if (problems.Count > 0)
        {
            return Refuse(LearningErrors.Invalid(problems));
        }

        var latest = await log.FindAsync(command.Id, cancellationToken);
        if (latest is null)
        {
            return Refuse(HypothesisErrors.NotFound(command.Id));
        }

        var from = HypothesisStateText.TryParse(latest.Row.State) ?? HypothesisState.Expired;
        ExperimentOutcome? outcome = null;
        if (command.To == HypothesisState.Accepted && latest.Row.ExperimentId is { } experimentId && await ledger.FindAsync(experimentId, cancellationToken) is { } stored)
        {
            outcome = stored.Experiment.Outcome;
        }

        var refusals = HypothesisLifecycle.Refusals(from, command.To, new HypothesisFacts(HypothesisRows.IsOwner(command.Actor), command.Autonomy, outcome));
        if (refusals.Count > 0)
        {
            return Refuse(HypothesisErrors.MoveRefused(refusals));
        }

        var appended = await log.AppendAsync(HypothesisRows.Next(latest, command.To, command.Reason, command.Actor, time.GetUtcNow()), cancellationToken);
        if (appended.Status != HypothesisAppendStatus.Created)
        {
            return Refuse(HypothesisErrors.Stale());
        }

        var fromCode = from.Code();
        var toCode = command.To.Code();
        HypothesisLog.Moved(logger, command.Id, fromCode, toCode);
        var text = command.To == HypothesisState.Accepted
            ? "Accepted: the verdict is recorded. Nothing was changed; applying it is your own act, recorded as a changelog row."
            : $"Moved to {command.To.Code()}.";
        return Result.Success(new HypothesisMoveOutcome(command.Id, command.To.Code(), text));
    }

    private Result<HypothesisMoveOutcome> Refuse(Error error)
    {
        HypothesisLog.Refused(logger, error.Code);
        return Result.Failure<HypothesisMoveOutcome>(error);
    }
}
