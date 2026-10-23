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
/// Replays the drift monitors over stored runs (spec P6-09, ADR 0074). Read-only: it records nothing, changes no cell state and writes no changelog row. With a model it also scores the outcomes the model was not trained on (dated after its last
/// training trade) through the hash-checked model file, in the order their labels became known.
/// </summary>
internal sealed class ReplayDriftHandler(
    IRunStore runs,
    ITradeFeatureStore features,
    IModelRegistry registry,
    IModelFiles files,
    IModelScorerLoader loader,
    TimeProvider time,
    ILogger<ReplayDriftHandler> logger) : IQueryHandler<ReplayDriftQuery, Result<DriftReport>>
{
    private static readonly string[] Styles = ["intraday", "swing"];

    public async ValueTask<Result<DriftReport>> HandleAsync(ReplayDriftQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var problems = Check(query);
        if (problems.Count > 0)
        {
            DriftLog.Refused(logger, LearningErrors.InvalidCode);
            return Result.Failure<DriftReport>(LearningErrors.Invalid(problems));
        }

        var started = time.GetTimestamp();
        var read = await CellSourceReader.ReadAsync(runs, query.Sources, cancellationToken);
        if (read.IsFailure)
        {
            return Refuse(read.Error);
        }

        if (read.Value.Trades.Count == 0)
        {
            return Refuse(LearningErrors.Invalid([new FieldError("sources", "The runs hold no closed trade.")]));
        }

        var latest = SessionClock.DateOf(read.Value.Trades.Max(t => t.Trade.ExitEpochSeconds));
        var from = latest.AddMonths(-query.MonitorMonths);
        var monitorFrom = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.FromMinutes(330)).ToUnixTimeSeconds();
        var cells = read.Value.Trades
            .GroupBy(t => t.CellKey, StringComparer.Ordinal)
            .Select(g => new DriftCellInput(g.Key, [.. g.Select(t => new DriftObservation(t.Trade.Key, t.Trade.ExitEpochSeconds, ResultOf(t.Trade)))]))
            .ToList();

        string? subject = null;
        IReadOnlyList<CalibrationObservation>? outcomes = null;
        var note = string.Empty;
        if (query.ModelId is { } id)
        {
            var scored = await ScoreAsync(query, id, cancellationToken);
            if (scored.IsFailure)
            {
                return Refuse(scored.Error);
            }

            (subject, outcomes, note) = scored.Value;
        }

        var report = DriftReplay.Run(cells, monitorFrom, query.Options, subject, outcomes, note);
        if (report.IsFailure)
        {
            return Refuse(report.Error);
        }

        var events = report.Value.Cells.Sum(c => c.Events.Count) + (report.Value.Calibration?.Events.Count ?? 0);
        var elapsedMs = (long)time.GetElapsedTime(started).TotalMilliseconds;
        DriftLog.Completed(logger, report.Value.Cells.Count, events, report.Value.Proposals.Count, elapsedMs);
        return report;
    }

    private async ValueTask<Result<(string Subject, IReadOnlyList<CalibrationObservation> Outcomes, string Note)>> ScoreAsync(ReplayDriftQuery query, long id, CancellationToken cancellationToken)
    {
        var stored = await registry.FindAsync(id, cancellationToken);
        if (stored is null)
        {
            return Result.Failure<(string, IReadOnlyList<CalibrationObservation>, string)>(ModelErrors.NotFound(id));
        }

        var m = stored.Model;
        var bytes = await files.ReadAsync(m.Sha256, cancellationToken);
        if (bytes.IsFailure)
        {
            return Result.Failure<(string, IReadOnlyList<CalibrationObservation>, string)>(bytes.Error);
        }

        var scorer = loader.Load(bytes.Value, m.Sha256);
        if (scorer.IsFailure)
        {
            return Result.Failure<(string, IReadOnlyList<CalibrationObservation>, string)>(scorer.Error);
        }

        // The runs are read a second time here (the cells above read them for the cell monitors); simple over fast, as the verb is occasional.
        var loaded = await TrainingDataLoader.LoadAsync(runs, features, query.Sources, query.CellOptions, query.Required, cancellationToken);
        if (loaded.IsFailure)
        {
            return Result.Failure<(string, IReadOnlyList<CalibrationObservation>, string)>(loaded.Error);
        }

        var data = loaded.Value.Data;
        if (data.FeatureVersion != m.FeatureVersion || TradeFeatureCatalog.Names.Count != m.FeatureWidth)
        {
            return Result.Failure<(string, IReadOnlyList<CalibrationObservation>, string)>(LearningErrors.Invalid([new FieldError("modelId", "The model reads another feature version than the training data of these runs.")]));
        }

        var unseen = data.Rows.Where(r => r.TradeDate > m.ToDate).ToList();
        var outcomes = unseen
            .Select(r => new CalibrationObservation(
                r.TradeKey,
                r.ResolvedEpochSeconds,
                scorer.Value.Score(new ValidationRow(r.TradeKey, r.Label, r.R, r.TradeDate, r.DecisionEpochSeconds, r.ResolvedEpochSeconds, loaded.Value.CellOf[r.TradeKey], [.. r.Values])),
                r.Label))
            .ToList();
        var note = string.Create(
            CultureInfo.InvariantCulture,
            $"Only trades dated after {m.ToDate:yyyy-MM-dd}, the last day the model trained on, are read ({unseen.Count} of {data.Rows.Count}); older trades would be scored in-sample and hide drift. This ignores --monitor-months: the unseen outcomes can reach back before the monitored period and include the model's hold-out rows.{(m.HoldOutStart is null ? " The model kept no hold-out." : string.Empty)}");
        return Result.Success<(string, IReadOnlyList<CalibrationObservation>, string)>(("model:" + id.ToString(CultureInfo.InvariantCulture), outcomes, note));
    }

    /// <summary>The result in the unit of the cell: net when the trade was charged, else from the filled prices (as the cell statistics read it).</summary>
    private static double ResultOf(SimTrade t) => t.Costs == CostBasis.Charged && t.Costing is not null ? t.Costing.RNet : t.RFilled;

    private Result<DriftReport> Refuse(Error error)
    {
        DriftLog.Refused(logger, error.Code);
        return Result.Failure<DriftReport>(error);
    }

    private static List<FieldError> Check(ReplayDriftQuery query)
    {
        var problems = new List<FieldError>();
        if (query.Options is null || query.CellOptions is null)
        {
            problems.Add(new FieldError("options", "The options are missing."));
            return problems;
        }

        problems.AddRange(query.Options.Problems().Select(p => new FieldError("options", p)));
        problems.AddRange(query.CellOptions.Problems().Select(p => new FieldError("cellOptions", p)));
        if (query.MonitorMonths is < 1 or > 120)
        {
            problems.Add(new FieldError("monitorMonths", "Monitored months are 1 to 120."));
        }

        if (query.ModelId is <= 0)
        {
            problems.Add(new FieldError("modelId", "A model id is positive."));
        }

        if (query.Sources is null || query.Sources.Count is < 1 or > ComputeCellStatsHandler.MaxSources)
        {
            problems.Add(new FieldError("sources", $"Name 1 to {ComputeCellStatsHandler.MaxSources} runs."));
            return problems;
        }

        foreach (var s in query.Sources)
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

        if (query.Sources.Select(s => s.RunKey).Distinct(StringComparer.Ordinal).Count() != query.Sources.Count)
        {
            problems.Add(new FieldError("sources", "A run is named twice."));
        }

        return problems;
    }
}
