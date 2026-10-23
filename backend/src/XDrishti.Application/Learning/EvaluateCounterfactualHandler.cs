using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>
/// Evaluates the frozen-model counterfactual of the running trainer's recipe (spec P6-12, ADR 0077): the hold-out is cut before anything is scored and stated. Read-only: it stores nothing and applies nothing. No champion is needed; the question is about the recipe.
/// </summary>
internal sealed class EvaluateCounterfactualHandler(
    IRunStore runs,
    ITradeFeatureStore features,
    IModelTrainer trainer,
    TimeProvider time,
    ILogger<EvaluateCounterfactualHandler> logger) : IQueryHandler<EvaluateCounterfactualQuery, Result<CounterfactualReport>>
{
    public async ValueTask<Result<CounterfactualReport>> HandleAsync(EvaluateCounterfactualQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var problems = TrainingDataLoader.Check(query.Sources, query.Options, query.Required, "cli:xd");
        if (query.Validation is not { } validation || query.Counterfactual is not { } options)
        {
            problems.Add(new FieldError("options", "The validation or counterfactual options are missing."));
            CounterfactualLog.Refused(logger, LearningErrors.InvalidCode);
            return Result.Failure<CounterfactualReport>(LearningErrors.Invalid(problems));
        }

        problems.AddRange(validation.Problems().Concat(options.Problems()).Select(p => new FieldError("options", p)));
        if (query.HoldOutMonths is < 0 or > 120)
        {
            problems.Add(new FieldError("holdOutMonths", "Hold-out months are 0 to 120."));
        }

        if (problems.Count > 0)
        {
            CounterfactualLog.Refused(logger, LearningErrors.InvalidCode);
            return Result.Failure<CounterfactualReport>(LearningErrors.Invalid(problems));
        }

        var started = time.GetTimestamp();
        var loaded = await TrainingDataLoader.LoadAsync(runs, features, query.Sources, query.Options, query.Required, cancellationToken);
        if (loaded.IsFailure)
        {
            return Refuse(loaded.Error);
        }

        var data = loaded.Value.Data;
        var rows = data.Rows
            .Select(r => new ValidationRow(r.TradeKey, r.Label, r.R, r.TradeDate, r.DecisionEpochSeconds, r.ResolvedEpochSeconds, loaded.Value.CellOf[r.TradeKey], [.. r.Values]))
            .ToList();
        string holdNote;
        if (query.HoldOutMonths > 0)
        {
            var split = HoldOut.Reserve(rows, query.HoldOutMonths);
            if (split.IsFailure)
            {
                return Refuse(split.Error);
            }

            rows = [.. split.Value.Development];
            validation = validation with { HoldOutStart = split.Value.Start };
            holdNote = $"The last {query.HoldOutMonths} months (from {split.Value.Start:yyyy-MM-dd}, {split.Value.Reserved.Count} trades, {split.Value.Purged} more purged) are held out and were not looked at.";
        }
        else
        {
            holdNote = "No hold-out was kept: every trade was used in the search.";
        }

        var evaluated = CounterfactualEvaluator.Evaluate(rows, trainer.Candidate, options, validation);
        if (evaluated.IsFailure)
        {
            return Refuse(evaluated.Error);
        }

        var report = evaluated.Value with { Limits = [.. evaluated.Value.Limits, holdNote, $"Model recipe: {trainer.Candidate.Name} ({trainer.Parameters}); training set {data.Id}."] };
        var elapsedMs = (long)time.GetElapsedTime(started).TotalMilliseconds;
        var outcome = report.Outcome.ToString();
        var judgedRows = report.Judged?.Rows ?? 0;
        CounterfactualLog.Completed(logger, outcome, report.Windows.Count, judgedRows, elapsedMs);
        return Result.Success(report);
    }

    private Result<CounterfactualReport> Refuse(Error error)
    {
        CounterfactualLog.Refused(logger, error.Code);
        return Result.Failure<CounterfactualReport>(error);
    }
}
