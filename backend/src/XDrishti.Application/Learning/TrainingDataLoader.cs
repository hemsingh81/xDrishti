using System.Globalization;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Learning;

/// <summary>The training data of stored runs with what was learnt on the way (P6-04, shared with P6-07).</summary>
/// <param name="Data">The joined data.</param>
/// <param name="Set">The labelled set the data came from.</param>
/// <param name="Sources">The sources as text, in key order.</param>
/// <param name="CellOf">The cell key of each trade key.</param>
internal sealed record LoadedTrainingData(TrainingData Data, TrainingSet Set, string Sources, IReadOnlyDictionary<string, string> CellOf);

/// <summary>
/// Builds the training data of stored runs (spec P6-04, ADR 0066): labels the closed trades (P6-03), reads their stored decision-time features, computes each trade's cell statistics as of the day before its decision (P6-01, point in time) and joins them with the
/// stated rule for unknowns. Shared by the training-set record (P6-04) and the model registry (P6-07), so both see the same rows and the same id.
/// </summary>
internal static class TrainingDataLoader
{
    private static readonly string[] Styles = ["intraday", "swing"];

    public static async ValueTask<Result<LoadedTrainingData>> LoadAsync(IRunStore runs, ITradeFeatureStore features, IReadOnlyList<CellSource> sources, CellStatsOptions options, IReadOnlyList<string>? required, CancellationToken cancellationToken)
    {
        var read = await CellSourceReader.ReadAsync(runs, sources, cancellationToken);
        if (read.IsFailure)
        {
            return Result.Failure<LoadedTrainingData>(read.Error);
        }

        var runKeys = sources.Select(s => s.RunKey).ToList();
        var set = TrainingSetBuilder.Build(runKeys, read.Value.Trades.Select(t => t.Trade));
        if (set.IsFailure)
        {
            return Result.Failure<LoadedTrainingData>(set.Error);
        }

        var cellOf = read.Value.Trades.ToDictionary(t => t.Trade.Key, t => (t.CellKey, Date: SessionClock.DateOf(t.Trade.DecisionEpochSeconds - 60)), StringComparer.Ordinal);
        var asks = set.Value.Labels.Select(l => new CellAsk(l.Key, cellOf[l.Key].CellKey, cellOf[l.Key].Date)).ToList();
        var cells = PointInTimeCells.Compute(read.Value.Cells, asks, options);
        var stored = await features.ReadAsync(runKeys, TradeFeatureCatalog.Version, cancellationToken);
        var text = string.Join(',', sources.OrderBy(s => s.RunKey, StringComparer.Ordinal).Select(s => $"{s.RunKey}:{s.Style}:{s.Exit}"));
        var provenance = string.Create(CultureInfo.InvariantCulture, $"{text};{options.RecencyHalfLifeDays:R};{options.PriorStrength:R};{options.MinTrades};{options.CredibleLevel:R}");
        var decisionOf = read.Value.Trades.ToDictionary(t => t.Trade.Key, t => t.Trade.DecisionEpochSeconds, StringComparer.Ordinal);
        var matching = stored.Where(f => decisionOf.TryGetValue(f.TradeKey, out var at) && at == f.DecisionEpochSeconds).ToList();
        var data = TrainingDataBuilder.Build(set.Value, matching, cells, required, provenance);
        return data.IsFailure
            ? Result.Failure<LoadedTrainingData>(data.Error)
            : Result.Success(new LoadedTrainingData(data.Value, set.Value, text, cellOf.ToDictionary(p => p.Key, p => p.Value.CellKey, StringComparer.Ordinal)));
    }

    /// <summary>The problems of the sources, options and required names before anything is read.</summary>
    public static List<FieldError> Check(IReadOnlyList<CellSource>? sources, CellStatsOptions? options, IReadOnlyList<string>? required, string actor)
    {
        var problems = new List<FieldError>();
        if (options is null)
        {
            problems.Add(new FieldError("options", "The options are missing."));
            return problems;
        }

        foreach (var p in options.Problems())
        {
            problems.Add(new FieldError("options", p));
        }

        if (sources is null || sources.Count is < 1 or > ComputeCellStatsHandler.MaxSources)
        {
            problems.Add(new FieldError("sources", $"Name 1 to {ComputeCellStatsHandler.MaxSources} runs."));
            return problems;
        }

        if (string.IsNullOrWhiteSpace(actor) || actor.Length > 100)
        {
            problems.Add(new FieldError("actor", "The actor is required and at most 100 characters."));
        }

        foreach (var s in sources)
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

        if (sources.Select(s => s.RunKey).Distinct(StringComparer.Ordinal).Count() != sources.Count)
        {
            problems.Add(new FieldError("sources", "A run is named twice."));
        }

        if (required is not null)
        {
            foreach (var name in required.Where(n => TradeFeatureCatalog.IndexOf(n) < 0))
            {
                problems.Add(new FieldError("required", $"'{name}' is not a feature of catalog {TradeFeatureCatalog.Version}."));
            }
        }

        return problems;
    }
}
