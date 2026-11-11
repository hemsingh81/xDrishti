using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.History;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.SimAll;
using XDrishti.Application.Market.Actions;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Domain.Research.SimAll;

namespace XDrishti.Application.Learning.SimTraining;

/// <summary>
/// Trains from a completed simulate-everything job (spec AU-13, ADR 0243 point 4, ADR 0249). Walk-forward only: the folds are the purged, anchored, quarterly folds of ADR 0067 plus the embargo, and no trade whose label horizon reaches the hold-out block enters at all. The runs are
/// read through <see cref="ITrainingRunReader"/> only (never the evidence run store); the features are the stored decision-time features; the cell statistics go to their own <c>training_sim</c> basis. The model is registered as a challenger and its verdict is capped below Eligible, so
/// this path can never produce a champion; it designates nothing, promotes nothing and never touches <c>MlMayInfluence</c>. Too little to train on answers NOT_EVALUABLE naming the missing quantity. The same inputs give the same input hash and a second run writes nothing.
/// </summary>
internal sealed class TrainFromSimulationHandler(
    ISimAllStore jobs,
    ITrainingRunReader reader,
    ISimTrainingStore store,
    IBarStore bars,
    IBackfillSeams seams,
    IAdjustmentFactors adjustments,
    ITradingCalendar calendars,
    ITradeFeatureExtractor extractor,
    ITradeFeatureStore features,
    ITrainingSetStore trainingSets,
    IModelTrainer trainer,
    IModelFiles files,
    IModelRegistry registry,
    SimTrainingSettings settings,
    TimeProvider time,
    ILogger<TrainFromSimulationHandler> logger) : ICommandHandler<TrainFromSimulationCommand, SimTrainingView>
{
    internal const string Actor = "system:sim-training";
    private const string Style = "swing"; // join keys of the point-in-time cells only; they carry no meaning here
    private const string ExitLabel = "simulated";

    public async ValueTask<Result<SimTrainingView>> HandleAsync(TrainFromSimulationCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!settings.Enabled)
        {
            return Refuse(SimTrainingErrors.Disabled);
        }

        if (await jobs.GetAsync(command.JobId, cancellationToken) is not { } job)
        {
            return Refuse(SimTrainingErrors.JobNotFound(command.JobId));
        }

        if (job.State is not (SimAllStates.Completed or SimAllStates.CompletedWithFailures))
        {
            return Refuse(SimTrainingErrors.JobNotFinished(job.Id, job.State));
        }

        var finished = await jobs.ListFinishedAsync(job.Id, cancellationToken);
        var inputHash = InputHash(job, finished.RunKeys);
        if (await store.FindAsync(inputHash, cancellationToken) is { } existing)
        {
            return Result.Success(SimTrainingView.Of(existing, true));
        }

        // headers first: the runs the training reader does not know as training runs are left out, and the row total decides the thinning
        var runs = new List<TrainingRunHeader>();
        foreach (var key in finished.RunKeys)
        {
            if (await reader.FindAsync(key, cancellationToken) is { } header)
            {
                runs.Add(header);
            }
        }

        var notFinished = finished.NotFinished + (finished.RunKeys.Count - runs.Count);
        var sampleEvery = (int)Math.Max(1, (runs.Sum(r => (long)r.TradeCount) + settings.MaxRows - 1) / settings.MaxRows);
        var loaded = await LoadAsync(job, runs, sampleEvery, cancellationToken);
        if (loaded.IsFailure)
        {
            return Refuse(loaded.Error);
        }

        var data = loaded.Value;
        var embargoDays = SimTrainingRules.EmbargoDays(settings.EmbargoSessions, job.WindowFrom, job.WindowTo, (await calendars.GetAsync(cancellationToken)).IsTradingDay);
        var now = time.GetUtcNow();
        var cells = data.CellStats.Select(r => new TrainingSimCell(r.Cell.Key, r.Cell.Strategy, r.Cell.Timeframe, r.Cell.Side, r.Window.Code(), r.Costed, r.N, r.Wins, r.EffectiveN, r.WinRatePosterior, r.WinRateLower, r.WinRateUpper, r.ExpectancyR, r.Enough)).ToList();
        var common = new Common(job, inputHash, runs.Count, notFinished, sampleEvery, data.HeldOut, embargoDays, now);

        SimTrainingRecord record;
        if (data.Rows.Count == 0 || data.Training is null)
        {
            record = NotEvaluable(common, SimTrainingRules.NotEvaluable(0, settings.MinRows, 0, ValidationOptions.DefaultMinFolds, null, null)!, 0, 0);
        }
        else
        {
            record = await TrainAsync(common, data, cancellationToken);
        }

        var written = await store.StoreAsync(record, job.WindowTo, cells, cancellationToken);
        SimTrainingLog.Done(logger, job.Id, record.State, record.Rows, record.FoldsUsed, written ? "stored" : "already stored");
        return Result.Success(SimTrainingView.Of(record, !written));
    }

    private async ValueTask<SimTrainingRecord> TrainAsync(Common c, Loaded data, CancellationToken cancellationToken)
    {
        var training = data.Training!;
        var rows = data.Rows;
        var trainingSetId = training.Id;
        cancellationToken.ThrowIfCancellationRequested(); // a cancelled train stores nothing more (a training set already stored is content-addressed and harmless)
        await trainingSets.StoreAsync(new TrainingSetToStore(training, $"sim-all:{c.Job.Id:D}", data.Options, data.Unresolved, data.NoTarget, Actor, c.Now), cancellationToken);

        var validation = SimTrainingRules.Options(c.EmbargoDays, 1);
        var from = rows.Min(r => r.TradeDate);
        var to = rows.Max(r => r.TradeDate);
        if (SimTrainingRules.NotEvaluable(rows.Count, settings.MinRows, int.MaxValue, 0, from, to) is { } fewRows)
        {
            return NotEvaluable(c, fewRows, rows.Count, 0);
        }

        var trained = trainer.Train(rows);
        if (trained.IsFailure)
        {
            return NotEvaluable(c, "model: " + trained.Error.Message, rows.Count, 0);
        }

        var model = trained.Value;
        var others = await registry.CountOtherModelsAsync(training.FeatureVersion, model.Sha256, cancellationToken);
        validation = validation with { Trials = Math.Max(validation.Trials, others + 1) };
        var compared = WalkForwardValidator.Compare(rows, [trainer.Candidate], validation);
        if (compared.IsFailure)
        {
            return NotEvaluable(c, "walk-forward folds: " + compared.Error.Message, rows.Count, 0);
        }

        var report = compared.Value;
        if (SimTrainingRules.NotEvaluable(rows.Count, settings.MinRows, report.Results.Count, ValidationOptions.DefaultMinFolds, from, to) is { } fewFolds)
        {
            return NotEvaluable(c, fewFolds, rows.Count, report.Folds.Count, report.Results.Count);
        }

        var verdict = report.Verdicts.Single(v => v.Name == trainer.Candidate.Name);
        var pooled = report.Pooled.Single(p => p.Name == trainer.Candidate.Name).Metrics;
        var baseline = report.Pooled.Where(p => p.Name != trainer.Candidate.Name).OrderBy(p => p.Metrics.LogLoss).First();
        var reasons = verdict.Reasons.ToList();
        var outcome = verdict.Outcome;
        if (outcome == ValidationOutcome.Eligible)
        {
            outcome = ValidationOutcome.Inconclusive; // a model learnt from simulated trades is a challenger at most: it cannot meet the champion rule by this path
            reasons.Add("Trained from the simulation: the verdict is capped below Eligible, so this model can only stay a challenger.");
        }

        var cc = CultureInfo.InvariantCulture;
        var summary = string.Create(cc, $"Trained from simulation job {c.Job.Id:D}: {rows.Count} closed trades ({c.Runs} runs, {c.NotFinished} chunks not finished, every {c.SampleEvery}. trade kept), window {c.Job.WindowFrom:yyyy-MM-dd} to {c.Job.WindowTo:yyyy-MM-dd}. "
            + $"Walk-forward: {report.Results.Count} of {report.Folds.Count} quarterly folds used, embargo {settings.EmbargoSessions} sessions ({c.EmbargoDays} days) and purge by label horizon. "
            + $"Out-of-fold log loss {pooled.LogLoss:F4} against {baseline.Metrics.LogLoss:F4} for the best baseline ({baseline.Name}); Brier {pooled.Brier:F4} against {baseline.Metrics.Brier:F4}. Verdict {outcome} (challenger only, never promoted by this path). "
            + $"The hold-out block from {c.Job.HoldOutStart:yyyy-MM-dd} was not read: {c.HeldOut} trades reaching it were left out whole.");
        var parameters = string.Create(cc, $"trainer: {trainer.Parameters}; source: training_sim job={c.Job.Id:D}; embargo_sessions={settings.EmbargoSessions} embargo_days={c.EmbargoDays} min_train_months={validation.MinTrainMonths} test_months={validation.TestMonths} min_train_trades={validation.MinTrainTrades} trials={validation.Trials}; sample_every={c.SampleEvery}");
        var reportText = report.ToText(rows[0].Features.Length == TradeFeatureCatalog.Names.Count ? TradeFeatureCatalog.Names : null) + summary + "\n";
        var versionKey = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"model-version/1\n{model.Sha256}\n{trainingSetId}\n{parameters}\n{reportText}")));
        var registration = new ModelToRegister(
            versionKey, model.Sha256, model.Bytes.Length, model.Trainer, model.Note, trainingSetId, training.FeatureVersion, model.Width, model.FitRows, model.CalibrationRows, rows.Count, from, to, c.Job.HoldOutStart,
            outcome, verdict.UsedFolds, verdict.FoldsBeatingBoth, verdict.FoldsNeeded, verdict.PooledBeatsBoth, verdict.PooledEcePp, pooled.LogLoss, pooled.Brier, string.Join('\n', reasons), reportText, parameters,
            model.CalibrationScores, model.CalibrationValues, model.Importance, validation.Trials, 0, c.Now, Actor);
        cancellationToken.ThrowIfCancellationRequested(); // an orphan model file is content-addressed and harmless, but a cancelled train writes no more
        var written = await files.WriteAsync(model.Sha256, model.Bytes, cancellationToken);
        if (written.IsFailure)
        {
            return NotEvaluable(c, "model file: " + written.Error.Message, rows.Count, report.Folds.Count, report.Results.Count);
        }

        var registered = await registry.RegisterAsync(registration, cancellationToken);
        return new SimTrainingRecord(c.InputHash, c.Job.Id, SimTrainingView.Trained, null, c.Runs, c.NotFinished, rows.Count, c.SampleEvery, report.Results.Count, report.Folds.Count, settings.EmbargoSessions, c.EmbargoDays,
            c.Job.WindowFrom, c.Job.WindowTo, c.Job.HoldOutStart, pooled.LogLoss, baseline.Metrics.LogLoss, baseline.Name, pooled.Brier, baseline.Metrics.Brier, outcome.ToString(), registered.Id, trainingSetId, summary, c.Now);
    }

    private SimTrainingRecord NotEvaluable(Common c, string missing, int rows, int foldsTotal, int foldsUsed = 0) =>
        new(c.InputHash, c.Job.Id, SimTrainingView.NotEvaluableState, missing, c.Runs, c.NotFinished, rows, c.SampleEvery, foldsUsed, foldsTotal, settings.EmbargoSessions, c.EmbargoDays, c.Job.WindowFrom, c.Job.WindowTo, c.Job.HoldOutStart,
            null, null, null, null, null, null, null, null,
            string.Create(CultureInfo.InvariantCulture, $"NOT_EVALUABLE: {missing}. No model was trained. The hold-out block from {c.Job.HoldOutStart:yyyy-MM-dd} was not read; {c.HeldOut} trades reaching it were left out."), c.Now);

    /// <summary>Extracts the features of each run, reads its closed trades through the training reader, leaves out what reaches the hold-out and thins above the row bound, then builds the labels, the cells and the joined rows.</summary>
    private async ValueTask<Result<Loaded>> LoadAsync(SimAllJob job, IReadOnlyList<TrainingRunHeader> runs, int sampleEvery, CancellationToken cancellationToken)
    {
        var trades = new List<SimTrade>();
        var matching = new List<TradeFeatureRow>();
        var heldOut = 0;
        foreach (var run in runs)
        {
            // one run at a time: only the admitted, sampled trades are kept, only their features are extracted, and only this run's stored features are read, so memory is bounded by MaxRows and one run
            var kept = new List<SimTrade>();
            await foreach (var runTrade in reader.StreamTradesAsync(run.RunKey, cancellationToken))
            {
                var t = runTrade.Trade;
                if (!SimTrainingRules.Admits(t.TradeDate, t.ExitEpochSeconds, job.WindowTo, job.HoldOutStart))
                {
                    heldOut++;
                    continue;
                }

                if (sampleEvery == 1 || Bucket(t.Key) % sampleEvery == 0)
                {
                    kept.Add(t);
                }
            }

            if (kept.Count == 0)
            {
                continue;
            }

            await ExtractTradeFeaturesHandler.ExtractAsync(Replay(kept), run.RunKey, bars, seams, adjustments, calendars, extractor, features, time, cancellationToken);
            var decisionOf = kept.ToDictionary(t => t.Key, t => t.DecisionEpochSeconds, StringComparer.Ordinal);
            matching.AddRange((await features.ReadAsync([run.RunKey], TradeFeatureCatalog.Version, cancellationToken)).Where(f => decisionOf.TryGetValue(f.TradeKey, out var at) && at == f.DecisionEpochSeconds));
            trades.AddRange(kept);
        }

        var options = new CellStatsOptions();
        var inputs = new Dictionary<string, (CellId Cell, bool Costed, List<CellTrade> Trades)>(StringComparer.Ordinal);
        var cellOf = new Dictionary<string, (string CellKey, DateOnly Date)>(StringComparer.Ordinal);
        foreach (var t in trades)
        {
            var id = new CellId(t.StrategyId, t.TriggerTimeframe.ToCode(), t.Side.ToString().ToUpperInvariant(), Style, ExitLabel, Expression: Expression.Cash);
            var costed = t.Costs == CostBasis.Charged && t.Costing is not null;
            if (!inputs.TryGetValue(id.Key, out var cell))
            {
                cell = (id, costed, []);
                inputs[id.Key] = cell;
            }

            if (cell.Costed != costed)
            {
                return Result.Failure<Loaded>(LearningErrors.MixedBasis(id.Key));
            }

            cell.Trades.Add(new CellTrade(SessionClock.DateOf(t.ExitEpochSeconds), costed ? t.Costing!.RNet : t.RFilled, t.MaeR, t.MfeR, t.TradeDate));
            cellOf[t.Key] = (id.Key, SessionClock.DateOf(t.DecisionEpochSeconds - 60));
        }

        var cellInputs = inputs.Values.OrderBy(c => c.Cell.Key, StringComparer.Ordinal).Select(c => new CellInput(c.Cell, c.Costed, c.Trades)).ToList();
        var stats = cellInputs.Count == 0 ? [] : CellStatsCalculator.Compute(cellInputs, job.WindowTo, options);
        if (trades.Count == 0)
        {
            return Result.Success(new Loaded([], null, stats, options, heldOut, 0, 0));
        }

        var runKeys = runs.Select(r => r.RunKey).ToList();
        var set = TrainingSetBuilder.Build(runKeys, trades);
        if (set.IsFailure)
        {
            return set.Error.Code == "training_set.empty" ? Result.Success(new Loaded([], null, stats, options, heldOut, 0, 0)) : Result.Failure<Loaded>(set.Error);
        }

        var asks = set.Value.Labels.Select(l => new CellAsk(l.Key, cellOf[l.Key].CellKey, cellOf[l.Key].Date)).ToList();
        var pointInTime = PointInTimeCells.Compute(cellInputs, asks, options);
        var provenance = string.Create(CultureInfo.InvariantCulture, $"sim-all:{job.Id:D};{settings.KeyText()};{sampleEvery};{options.RecencyHalfLifeDays:R};{options.PriorStrength:R};{options.MinTrades};{options.CredibleLevel:R}");
        var built = TrainingDataBuilder.Build(set.Value, matching, pointInTime, null, provenance);
        if (built.IsFailure)
        {
            return built.Error.Code.EndsWith("empty", StringComparison.Ordinal) ? Result.Success(new Loaded([], null, stats, options, heldOut, set.Value.Unresolved, set.Value.NoTarget)) : Result.Failure<Loaded>(built.Error);
        }

        var validationRows = built.Value.Rows
            .Select(r => new ValidationRow(r.TradeKey, r.Label, r.R, r.TradeDate, r.DecisionEpochSeconds, r.ResolvedEpochSeconds, cellOf[r.TradeKey].CellKey, [.. r.Values]))
            .ToList();
        return Result.Success(new Loaded(validationRows, built.Value, stats, options, heldOut, set.Value.Unresolved, set.Value.NoTarget));
    }

    private static async IAsyncEnumerable<RunTrade> Replay(List<SimTrade> kept)
    {
        foreach (var t in kept)
        {
            await Task.Yield();
            yield return new RunTrade(t, []);
        }
    }

    private string InputHash(SimAllJob job, IReadOnlyList<string> runKeys)
    {
        var text = string.Create(CultureInfo.InvariantCulture, $"sim-training/1|{job.Id:D}|{job.WindowFrom:yyyy-MM-dd}|{job.WindowTo:yyyy-MM-dd}|{job.HoldOutStart:yyyy-MM-dd}|{settings.KeyText()}|{TradeFeatureCatalog.Version}|{trainer.Parameters}|{string.Join(',', runKeys)}");
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    /// <summary>A stable bucket of a trade key, so thinning keeps the same trades on every run.</summary>
    private static uint Bucket(string key) => BitConverter.ToUInt32(SHA256.HashData(Encoding.UTF8.GetBytes(key)), 0);

    private Result<SimTrainingView> Refuse(Error error)
    {
        SimTrainingLog.Refused(logger, error.Code);
        return Result.Failure<SimTrainingView>(error);
    }

    private sealed record Common(SimAllJob Job, string InputHash, int Runs, int NotFinished, int SampleEvery, int HeldOut, int EmbargoDays, DateTimeOffset Now);

    private sealed record Loaded(IReadOnlyList<ValidationRow> Rows, TrainingData? Training, IReadOnlyList<CellStatsRow> CellStats, CellStatsOptions Options, int HeldOut, int Unresolved, int NoTarget);
}

/// <summary>Reads the stored training of a job (spec AU-13). Read only.</summary>
internal sealed class GetSimTrainingHandler(ISimTrainingStore store) : IQueryHandler<GetSimTrainingQuery, Result<SimTrainingView>>
{
    public async ValueTask<Result<SimTrainingView>> HandleAsync(GetSimTrainingQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return await store.FindByJobAsync(query.JobId, cancellationToken) is { } record
            ? Result.Success(SimTrainingView.Of(record, true))
            : Result.Failure<SimTrainingView>(SimTrainingErrors.NotFound(query.JobId));
    }
}
