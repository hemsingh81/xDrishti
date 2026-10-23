using System.Globalization;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;

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
        var problems = Check(command);
        if (problems.Count > 0)
        {
            FeatureLog.TrainingSetRefused(logger, LearningErrors.InvalidCode);
            return Result.Failure<TrainingSetOutcome>(LearningErrors.Invalid(problems));
        }

        var started = time.GetTimestamp();
        var read = await CellSourceReader.ReadAsync(runs, command.Sources, cancellationToken);
        if (read.IsFailure)
        {
            FeatureLog.TrainingSetRefused(logger, read.Error.Code);
            return Result.Failure<TrainingSetOutcome>(read.Error);
        }

        var runKeys = command.Sources.Select(s => s.RunKey).ToList();
        var set = TrainingSetBuilder.Build(runKeys, read.Value.Trades.Select(t => t.Trade));
        if (set.IsFailure)
        {
            FeatureLog.TrainingSetRefused(logger, set.Error.Code);
            return Result.Failure<TrainingSetOutcome>(set.Error);
        }

        var cellOf = read.Value.Trades.ToDictionary(t => t.Trade.Key, t => (t.CellKey, Date: SessionClock.DateOf(t.Trade.DecisionEpochSeconds - 60)), StringComparer.Ordinal);
        var asks = set.Value.Labels.Select(l => new CellAsk(l.Key, cellOf[l.Key].CellKey, cellOf[l.Key].Date)).ToList();
        var cells = PointInTimeCells.Compute(read.Value.Cells, asks, command.Options);
        var stored = await features.ReadAsync(runKeys, TradeFeatureCatalog.Version, cancellationToken);
        var sources = string.Join(',', command.Sources.OrderBy(s => s.RunKey, StringComparer.Ordinal).Select(s => $"{s.RunKey}:{s.Style}:{s.Exit}"));
        var o = command.Options;
        var provenance = string.Create(CultureInfo.InvariantCulture, $"{sources};{o.RecencyHalfLifeDays:R};{o.PriorStrength:R};{o.MinTrades};{o.CredibleLevel:R}");
        var decisionOf = read.Value.Trades.ToDictionary(t => t.Trade.Key, t => t.Trade.DecisionEpochSeconds, StringComparer.Ordinal);
        var matching = stored.Where(f => decisionOf.TryGetValue(f.TradeKey, out var at) && at == f.DecisionEpochSeconds).ToList();
        var data = TrainingDataBuilder.Build(set.Value, matching, cells, command.Required, provenance);
        if (data.IsFailure)
        {
            FeatureLog.TrainingSetRefused(logger, data.Error.Code);
            return Result.Failure<TrainingSetOutcome>(data.Error);
        }

        var d = data.Value;
        bool written;
        try
        {
            written = await store.StoreAsync(new TrainingSetToStore(d, sources, command.Options, set.Value.Unresolved, set.Value.NoTarget, command.Actor, time.GetUtcNow()), cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            FeatureLog.TrainingSetFailed(logger);
            throw;
        }

        var unknown = d.UnknownByFeature.Where(p => p.Value > 0).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        var elapsedMs = (long)time.GetElapsedTime(started).TotalMilliseconds;
        FeatureLog.TrainingSetBuilt(logger, d.Id[..12], d.Rows.Count, d.Positives, d.DroppedNoFeatures, d.DroppedUnknown, written ? "stored" : "already stored", elapsedMs);
        return Result.Success(new TrainingSetOutcome(d.Id, d.Rows.Count, d.Positives, d.Negatives, set.Value.Unresolved, set.Value.NoTarget, d.DroppedNoFeatures, d.DroppedUnknown, unknown, !written));
    }

    private static readonly string[] Styles = ["intraday", "swing"];

    private static List<FieldError> Check(BuildTrainingSetCommand command)
    {
        var problems = new List<FieldError>();
        if (command.Options is null)
        {
            problems.Add(new FieldError("options", "The options are missing."));
            return problems;
        }

        foreach (var p in command.Options.Problems())
        {
            problems.Add(new FieldError("options", p));
        }

        if (command.Sources is null || command.Sources.Count is < 1 or > ComputeCellStatsHandler.MaxSources)
        {
            problems.Add(new FieldError("sources", $"Name 1 to {ComputeCellStatsHandler.MaxSources} runs."));
            return problems;
        }

        if (string.IsNullOrWhiteSpace(command.Actor) || command.Actor.Length > 100)
        {
            problems.Add(new FieldError("actor", "The actor is required and at most 100 characters."));
        }

        foreach (var s in command.Sources)
        {
            if (!RunRecordText.IsHex64(s.RunKey))
            {
                problems.Add(new FieldError("sources.runKey", "A run key is 64 lower-case hex characters."));
            }

            if (!Styles.Contains(s.Style, StringComparer.Ordinal))
            {
                problems.Add(new FieldError("sources.style", "The style is intraday or swing."));
            }

            if (new CellId("x", "x", "x", "x", s.Exit ?? string.Empty).Problems().Count > 0)
            {
                problems.Add(new FieldError("sources.exit", "The exit label is required, at most 64 characters and without '|'."));
            }
        }

        if (command.Sources.Select(s => s.RunKey).Distinct(StringComparer.Ordinal).Count() != command.Sources.Count)
        {
            problems.Add(new FieldError("sources", "A run is named twice."));
        }

        if (command.Required is not null)
        {
            foreach (var name in command.Required.Where(n => TradeFeatureCatalog.IndexOf(n) < 0))
            {
                problems.Add(new FieldError("required", $"'{name}' is not a feature of catalog {TradeFeatureCatalog.Version}."));
            }
        }

        return problems;
    }
}
