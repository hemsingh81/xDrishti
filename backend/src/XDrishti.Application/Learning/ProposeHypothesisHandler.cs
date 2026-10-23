using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>Writes a hypothesis down (spec P6-10, ADR 0076). Checks the knob against the stored baseline run, the grid for a grid source, and the open limit; runs and approves nothing.</summary>
internal sealed class ProposeHypothesisHandler(IRunStore runs, IHypothesisLog log, TimeProvider time, ILogger<ProposeHypothesisHandler> logger) : ICommandHandler<ProposeHypothesisCommand, HypothesisMoveOutcome>
{
    public async ValueTask<Result<HypothesisMoveOutcome>> HandleAsync(ProposeHypothesisCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var problems = Check(command);
        if (problems.Count > 0)
        {
            return Refuse(LearningErrors.Invalid(problems));
        }

        var header = await runs.FindAsync(command.BaselineRunKey, cancellationToken);
        var record = header is null ? null : await runs.ReadRecordTextAsync(command.BaselineRunKey, cancellationToken);
        if (header is null || record is null)
        {
            return Refuse(LearningErrors.RunNotFound(command.BaselineRunKey));
        }

        var definition = RunDefinition.TryRead(record, header.Fills, out var readProblem);
        if (definition is null)
        {
            return Refuse(LearningErrors.Invalid([new FieldError("baselineRunKey", readProblem!)]));
        }

        var baselineProblems = OneVariableRule.BaselineProblems(command.Knob, definition);
        if (baselineProblems.Count > 0)
        {
            return Refuse(LearningErrors.Invalid([.. baselineProblems.Select(p => new FieldError("knob", p))]));
        }

        if (command.Source == HypothesisSource.Grid && !command.Grid.Contains(definition.Parameters[command.Knob.Name], command.Knob.CandidateValue))
        {
            return Refuse(LearningErrors.Invalid([new FieldError("knob", "The candidate value is not on the grid around the baseline value.")]));
        }

        var open = await log.CountOpenAsync(cancellationToken);
        var refusals = HypothesisLifecycle.ProposalRefusals(new HypothesisFacts(HypothesisRows.IsOwner(command.Actor), AutonomyLevel.Supervised, null, open));
        if (refusals.Count > 0)
        {
            return Refuse(HypothesisErrors.OpenLimit(refusals));
        }

        var now = time.GetUtcNow();
        var row = new NewHypothesisRow(
            null, HypothesisState.Proposed.Code(), null, command.Source.ToString().ToLowerInvariant(), command.CellKey, command.Knob.Name, command.Knob.Kind.ToString(), command.Knob.BaselineValue, command.Knob.CandidateValue,
            command.Rationale, command.BaselineRunKey, null, null, "proposed", command.Actor, now);
        var appended = await log.ProposeAsync(row, HypothesisLifecycle.DefaultMaxOpen, cancellationToken);
        if (appended.Status != HypothesisAppendStatus.Created)
        {
            return Refuse(HypothesisErrors.OpenLimit(["The open limit was reached while this was handled."]));
        }

        HypothesisLog.Proposed(logger, appended.Id, row.Source);
        return Result.Success(new HypothesisMoveOutcome(appended.Id, row.State, "Proposed. Nothing is run: approve it, attach a candidate run made with the one knob changed, then judge it."));
    }

    private Result<HypothesisMoveOutcome> Refuse(Error error)
    {
        HypothesisLog.Refused(logger, error.Code);
        return Result.Failure<HypothesisMoveOutcome>(error);
    }

    private static List<FieldError> Check(ProposeHypothesisCommand c)
    {
        var problems = new List<FieldError>();
        if (c.Knob is null || c.Grid is null)
        {
            problems.Add(new FieldError("knob", "The knob and the grid rules are required."));
            return problems;
        }

        problems.AddRange(c.Knob.Problems().Select(p => new FieldError("knob", p)));
        if (!Enum.IsDefined(c.Source))
        {
            problems.Add(new FieldError("source", "The source is owner or grid."));
        }

        if (c.Source == HypothesisSource.Grid)
        {
            problems.AddRange(c.Grid.Problems().Select(p => new FieldError("grid", p)));
            if (c.Grid != new HypothesisGrid())
            {
                problems.Add(new FieldError("grid", "A grid hypothesis uses the fixed coarse grid (10 percent, 2 steps); another grid is an owner hypothesis."));
            }

            if (c.Knob.Kind != ExperimentKnobKind.Parameter)
            {
                problems.Add(new FieldError("knob", "The grid proposes values for a parameter only."));
            }
        }

        if (!RunRecordText.IsHex64(c.BaselineRunKey))
        {
            problems.Add(new FieldError("baselineRunKey", "A run key is 64 lower-case hex characters."));
        }

        if (string.IsNullOrWhiteSpace(c.Rationale) || c.Rationale.Length > HypothesisRows.MaxText)
        {
            problems.Add(new FieldError("rationale", "The rationale is required, at most 500 characters."));
        }

        if (c.CellKey is not null && CellId.TryParse(c.CellKey) is null)
        {
            problems.Add(new FieldError("cell", "A cell key is strategy|timeframe|side|style|exit|regime."));
        }

        if (!HypothesisRows.ValidActor(c.Actor))
        {
            problems.Add(new FieldError("actor", "The actor is required and at most 100 characters."));
        }

        return problems;
    }
}
