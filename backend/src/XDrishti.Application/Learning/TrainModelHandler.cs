using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>
/// Trains, validates and registers a model from stored runs (spec P6-07, ADR 0070). It builds the training data (P6-04) and keeps its record, cuts the hold-out (P6-05), judges the candidate against the baselines on purged walk-forward folds, trains the final model on
/// the development rows (P6-06), writes the file under its hash and registers it as a challenger with its verdict, whatever the verdict. The hold-out is in neither the verdict nor the model. The same inputs give the same hash and the same registration, which is written once.
/// </summary>
internal sealed class TrainModelHandler(
    IRunStore runs,
    ITradeFeatureStore features,
    ITrainingSetStore trainingSets,
    IModelTrainer trainer,
    IModelFiles files,
    IModelRegistry registry,
    TimeProvider time,
    ILogger<TrainModelHandler> logger) : ICommandHandler<TrainModelCommand, TrainModelOutcome>
{
    public async ValueTask<Result<TrainModelOutcome>> HandleAsync(TrainModelCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var problems = TrainingDataLoader.Check(command.Sources, command.Options, command.Required, command.Actor);
        if (command.Validation is null)
        {
            problems.Add(new FieldError("validation", "The validation options are missing."));
        }
        else
        {
            problems.AddRange(command.Validation.Problems().Select(p => new FieldError("validation", p)));
        }

        if (command.HoldOutMonths is < 0 or > 120)
        {
            problems.Add(new FieldError("holdOutMonths", "Hold-out months are 0 to 120."));
        }

        if (problems.Count > 0)
        {
            return Refuse<TrainModelOutcome>(LearningErrors.Invalid(problems));
        }

        var started = time.GetTimestamp();
        var loaded = await TrainingDataLoader.LoadAsync(runs, features, command.Sources, command.Options, command.Required, cancellationToken);
        if (loaded.IsFailure)
        {
            return Refuse<TrainModelOutcome>(loaded.Error);
        }

        var data = loaded.Value.Data;
        var now = time.GetUtcNow();
        try
        {
            await trainingSets.StoreAsync(new TrainingSetToStore(data, loaded.Value.Sources, command.Options, loaded.Value.Set.Unresolved, loaded.Value.Set.NoTarget, command.Actor, now), cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            ModelLog.Failed(logger);
            throw;
        }

        var rows = data.Rows
            .Select(r => new ValidationRow(r.TradeKey, r.Label, r.R, r.TradeDate, r.DecisionEpochSeconds, r.ResolvedEpochSeconds, loaded.Value.CellOf[r.TradeKey], [.. r.Values]))
            .ToList();
        var validation = command.Validation ?? new ValidationOptions();
        DateOnly? holdOutStart = null;
        var purged = 0;
        if (command.HoldOutMonths > 0)
        {
            var split = HoldOut.Reserve(rows, command.HoldOutMonths);
            if (split.IsFailure)
            {
                return Refuse<TrainModelOutcome>(split.Error);
            }

            rows = [.. split.Value.Development];
            holdOutStart = split.Value.Start;
            purged = split.Value.Purged;
            validation = validation with { HoldOutStart = holdOutStart };
        }

        // The model is made first: its hash tells which registered models are other trials. The trials are what the registry already holds for this feature version plus this model; the command line can only raise them.
        var trained = trainer.Train(rows);
        if (trained.IsFailure)
        {
            return Refuse<TrainModelOutcome>(trained.Error);
        }

        var model = trained.Value;
        var others = await registry.CountOtherModelsAsync(data.FeatureVersion, model.Sha256, cancellationToken);
        validation = validation with { Trials = Math.Max(validation.Trials, others + 1) };
        var compared = WalkForwardValidator.Compare(rows, [trainer.Candidate], validation);
        if (compared.IsFailure)
        {
            return Refuse<TrainModelOutcome>(compared.Error);
        }

        var report = compared.Value;
        var verdict = report.Verdicts.Single(v => v.Name == trainer.Candidate.Name);
        var pooled = report.Pooled.Single(p => p.Name == trainer.Candidate.Name).Metrics;
        var outcomeVerdict = verdict.Outcome;
        var reasons = verdict.Reasons.ToList();
        if (outcomeVerdict == ValidationOutcome.Eligible && validation.MinFoldsForVerdict < ValidationOptions.DefaultMinFolds)
        {
            outcomeVerdict = ValidationOutcome.Inconclusive;
            reasons.Add(string.Create(CultureInfo.InvariantCulture, $"The verdict was asked for with {validation.MinFoldsForVerdict} folds as the minimum; below {ValidationOptions.DefaultMinFolds} it cannot be Eligible."));
        }

        var reportText = report.ToText() + (holdOutStart is { } h
            ? string.Create(CultureInfo.InvariantCulture, $"The last {command.HoldOutMonths} months (from {h:yyyy-MM-dd}) are held out: neither the verdict nor the model has seen them. {purged} development trades were purged because their label horizon reaches into the hold-out.\n")
            : "No hold-out was kept: every trade was used in the verdict and in the model.\n")
            + string.Create(CultureInfo.InvariantCulture, $"Trials allowed for: {validation.Trials} (the models of this feature version in the registry, this one included, or the number given, if larger).\n")
            + (outcomeVerdict == verdict.Outcome ? string.Empty : "The verdict was capped at Inconclusive: the minimum number of folds asked for is below the floor.\n");
        var parameters = Describe(trainer.Parameters, validation, command);
        var registration = new ModelToRegister(
            VersionKey(model.Sha256, data.Id, parameters, reportText),
            model.Sha256,
            model.Bytes.Length,
            model.Trainer,
            model.Note,
            data.Id,
            data.FeatureVersion,
            model.Width,
            model.FitRows,
            model.CalibrationRows,
            rows.Count,
            rows.Min(r => r.TradeDate),
            rows.Max(r => r.TradeDate),
            holdOutStart,
            outcomeVerdict,
            verdict.UsedFolds,
            verdict.FoldsBeatingBoth,
            verdict.FoldsNeeded,
            verdict.PooledBeatsBoth,
            verdict.PooledEcePp,
            pooled.LogLoss,
            pooled.Brier,
            string.Join('\n', reasons),
            reportText,
            parameters,
            model.CalibrationScores,
            model.CalibrationValues,
            model.Importance,
            validation.Trials,
            purged,
            now,
            command.Actor);

        var written = await files.WriteAsync(model.Sha256, model.Bytes, cancellationToken);
        if (written.IsFailure)
        {
            return Refuse<TrainModelOutcome>(written.Error);
        }

        RegisteredModel registered;
        try
        {
            registered = await registry.RegisterAsync(registration, cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            ModelLog.Failed(logger);
            throw;
        }

        var elapsedMs = (long)time.GetElapsedTime(started).TotalMilliseconds;
        var outcome = outcomeVerdict.ToString();
        var hash = model.Sha256[..12];
        var what = registered.Created ? "registered" : "already registered";
        ModelLog.Registered(logger, registered.Id, hash, outcome, rows.Count, what, elapsedMs);
        return Result.Success(new TrainModelOutcome(registered.Id, model.Sha256, !registered.Created, data.Id, outcome, reasons, reportText, model.Trainer, model.Note, rows.Count, holdOutStart));
    }

    private Result<T> Refuse<T>(Error error)
    {
        ModelLog.Refused(logger, error.Code);
        return Result.Failure<T>(error);
    }

    private static string Describe(string trainerParameters, ValidationOptions v, TrainModelCommand command)
    {
        var c = CultureInfo.InvariantCulture;
        var cell = command.Options;
        return string.Create(c, $"trainer: {trainerParameters}; validation: min_train_months={v.MinTrainMonths} test_months={v.TestMonths} embargo_days={v.EmbargoDays} min_train_trades={v.MinTrainTrades} min_folds_for_verdict={v.MinFoldsForVerdict} folds_better_share={v.FoldsBetterShare:R} log_loss_margin={v.LogLossMargin:R} brier_margin={v.BrierMargin:R} max_calibration_error_pp={v.MaxCalibrationErrorPp:R} calibration_bins={v.CalibrationBins} trials={v.Trials}; hold_out_months={command.HoldOutMonths}; cell: half_life_days={cell.RecencyHalfLifeDays:R} prior_strength={cell.PriorStrength:R} min_trades={cell.MinTrades} credible_level={cell.CredibleLevel:R}; required: {string.Join(',', (command.Required ?? TradeFeatureCatalog.DefaultRequired).Order(StringComparer.Ordinal))}");
    }

    private static string VersionKey(string sha256, string trainingSetId, string parameters, string reportText) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"model-version/1\n{sha256}\n{trainingSetId}\n{parameters}\n{reportText}")));
}
