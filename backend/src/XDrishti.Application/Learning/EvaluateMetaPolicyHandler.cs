using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>
/// Evaluates the meta-labelling policy of the champion (spec P6-13, ADR 0075). Without a champion, or when the stored data or the trainer settings are not those the champion was made with, the answer is "no policy". Read-only: it stores nothing and applies nothing.
/// </summary>
internal sealed class EvaluateMetaPolicyHandler(
    IRunStore runs,
    ITradeFeatureStore features,
    IModelRegistry registry,
    IModelTrainer trainer,
    TimeProvider time,
    ILogger<EvaluateMetaPolicyHandler> logger) : IQueryHandler<EvaluateMetaPolicyQuery, Result<MetaPolicyReport>>
{
    public async ValueTask<Result<MetaPolicyReport>> HandleAsync(EvaluateMetaPolicyQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var problems = TrainingDataLoader.Check(query.Sources, query.Options, query.Required, "cli:xd");
        if (query.Validation is not { } validation || query.Policy is not { } policy)
        {
            problems.Add(new FieldError("options", "The validation or policy options are missing."));
            MetaPolicyLog.Refused(logger, LearningErrors.InvalidCode);
            return Result.Failure<MetaPolicyReport>(LearningErrors.Invalid(problems));
        }

        problems.AddRange(validation.Problems().Concat(policy.Problems()).Select(p => new FieldError("options", p)));

        if (query.HoldOutMonths is < 0 or > 120)
        {
            problems.Add(new FieldError("holdOutMonths", "Hold-out months are 0 to 120."));
        }

        if (problems.Count > 0)
        {
            MetaPolicyLog.Refused(logger, LearningErrors.InvalidCode);
            return Result.Failure<MetaPolicyReport>(LearningErrors.Invalid(problems));
        }

        var started = time.GetTimestamp();
        var listed = await registry.ListAsync(ModelRole.Champion, 1, cancellationToken);
        if (listed.Count == 0)
        {
            return NoPolicy(policy, "There is no champion model: nothing to turn into a policy (name one with xd learn designate).");
        }

        var champion = await registry.FindAsync(listed[0].Id, cancellationToken);
        if (champion is null)
        {
            return NoPolicy(policy, "The champion model could not be read.");
        }

        if (!champion.Model.Parameters.StartsWith($"trainer: {trainer.Parameters}; ", StringComparison.Ordinal))
        {
            return NoPolicy(policy, $"The champion (model {champion.Id}) was made with other trainer settings than the running trainer; the policy would judge a different model.");
        }

        var loaded = await TrainingDataLoader.LoadAsync(runs, features, query.Sources, query.Options, query.Required, cancellationToken);
        if (loaded.IsFailure)
        {
            return Refuse(loaded.Error);
        }

        var data = loaded.Value.Data;
        if (!string.Equals(data.Id, champion.Model.TrainingSetId, StringComparison.Ordinal))
        {
            return NoPolicy(policy, $"The stored runs make another training set than the champion's (model {champion.Id}); name the runs it was trained on.");
        }

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

        var evaluated = MetaPolicyEvaluator.Evaluate(rows, trainer.Candidate, policy, validation);
        if (evaluated.IsFailure)
        {
            return Refuse(evaluated.Error);
        }

        var hash = champion.Model.Sha256[..12];
        var report = evaluated.Value with { Limits = [.. evaluated.Value.Limits, holdNote, $"Champion: model {champion.Id} ({hash}), verdict {champion.Model.Verdict}."] };
        var elapsedMs = (long)time.GetElapsedTime(started).TotalMilliseconds;
        var outcome = report.Outcome.ToString();
        MetaPolicyLog.Completed(logger, outcome, report.Rows, report.UsedFolds, champion.Id, elapsedMs);
        return Result.Success(report);
    }

    private Result<MetaPolicyReport> NoPolicy(MetaPolicyOptions policy, string reason)
    {
        MetaPolicyLog.NoPolicy(logger, reason);
        return Result.Success(MetaPolicyReport.NoPolicy(reason, policy, ["Advisory: nothing uses a policy until P7."]));
    }

    private Result<MetaPolicyReport> Refuse(Error error)
    {
        MetaPolicyLog.Refused(logger, error.Code);
        return Result.Failure<MetaPolicyReport>(error);
    }
}
