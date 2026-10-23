using System.Globalization;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>The grid source (spec P6-10): values around a parameter of a stored baseline run, deterministic and bounded; nothing is written and no run is made.</summary>
internal sealed class GridHypothesesHandler(IRunStore runs) : IQueryHandler<GridHypothesesQuery, Result<GridHypothesesView>>
{
    public async ValueTask<Result<GridHypothesesView>> HandleAsync(GridHypothesesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var problems = new List<FieldError>();
        if (query.Grid is null || !RunRecordText.IsHex64(query.BaselineRunKey) || string.IsNullOrWhiteSpace(query.Parameter))
        {
            problems.Add(new FieldError("request", "A run key (64 hex), a parameter name and the grid rules are required."));
        }
        else
        {
            problems.AddRange(query.Grid.Problems().Select(p => new FieldError("grid", p)));
        }

        if (problems.Count > 0)
        {
            return Result.Failure<GridHypothesesView>(LearningErrors.Invalid(problems));
        }

        var header = await runs.FindAsync(query.BaselineRunKey, cancellationToken);
        var record = header is null ? null : await runs.ReadRecordTextAsync(query.BaselineRunKey, cancellationToken);
        if (header is null || record is null)
        {
            return Result.Failure<GridHypothesesView>(LearningErrors.RunNotFound(query.BaselineRunKey));
        }

        var definition = RunDefinition.TryRead(record, header.Fills, out var problem);
        if (definition is null)
        {
            return Result.Failure<GridHypothesesView>(LearningErrors.Invalid([new FieldError("baselineRunKey", problem!)]));
        }

        if (!definition.Parameters.TryGetValue(query.Parameter, out var current))
        {
            return Result.Failure<GridHypothesesView>(LearningErrors.Invalid([new FieldError("parameter", "The baseline run has no such parameter.")]));
        }

        return Result.Success(new GridHypothesesView(
            query.Parameter,
            current.ToString("R", CultureInfo.InvariantCulture),
            query.Grid!.Around(current),
            "Proposals only: a deterministic coarse grid around the current value, no model and no LLM. Each value is a hypothesis only when you propose it, and every tried value is a trial that raises the bar."));
    }
}
