using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Learning;

/// <summary>
/// Builds the training set of stored runs (spec P6-04, ADR 0066): labels the closed trades (P6-03), reads their stored decision-time features, computes each trade's cell statistics as of the day before its decision (P6-01, point in time), joins them with the
/// stated rule for unknowns and keeps the record. The same inputs give the same id and a second build writes nothing. Run the feature extraction first: labelled trades without features are dropped and counted.
/// </summary>
internal sealed class BuildTrainingSetHandler(IRunStore runs, ITradeFeatureStore features, ITrainingSetStore store, TimeProvider time, ILogger<BuildTrainingSetHandler> logger) : ICommandHandler<BuildTrainingSetCommand, TrainingSetOutcome>
{
    public async ValueTask<Result<TrainingSetOutcome>> HandleAsync(BuildTrainingSetCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var problems = TrainingDataLoader.Check(command.Sources, command.Options, command.Required, command.Actor);
        if (problems.Count > 0)
        {
            FeatureLog.TrainingSetRefused(logger, LearningErrors.InvalidCode);
            return Result.Failure<TrainingSetOutcome>(LearningErrors.Invalid(problems));
        }

        var started = time.GetTimestamp();
        var loaded = await TrainingDataLoader.LoadAsync(runs, features, command.Sources, command.Options, command.Required, cancellationToken);
        if (loaded.IsFailure)
        {
            FeatureLog.TrainingSetRefused(logger, loaded.Error.Code);
            return Result.Failure<TrainingSetOutcome>(loaded.Error);
        }

        var d = loaded.Value.Data;
        var set = loaded.Value.Set;
        bool written;
        try
        {
            written = await store.StoreAsync(new TrainingSetToStore(d, loaded.Value.Sources, command.Options, set.Unresolved, set.NoTarget, command.Actor, time.GetUtcNow()), cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            FeatureLog.TrainingSetFailed(logger);
            throw;
        }

        var unknown = d.UnknownByFeature.Where(p => p.Value > 0).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        var elapsedMs = (long)time.GetElapsedTime(started).TotalMilliseconds;
        FeatureLog.TrainingSetBuilt(logger, d.Id[..12], d.Rows.Count, d.Positives, d.DroppedNoFeatures, d.DroppedUnknown, written ? "stored" : "already stored", elapsedMs);
        return Result.Success(new TrainingSetOutcome(d.Id, d.Rows.Count, d.Positives, d.Negatives, set.Unresolved, set.NoTarget, d.DroppedNoFeatures, d.DroppedUnknown, unknown, !written));
    }
}
