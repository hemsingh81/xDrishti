using System.Diagnostics;
using Microsoft.ML;
using Microsoft.ML.Calibrators;
using Microsoft.ML.Data;
using Microsoft.ML.Trainers.FastTree;
using Microsoft.ML.Trainers.LightGbm;
using Microsoft.ML.Transforms;

namespace XDrishti.MlSpike;

public sealed record SpikeOptions(string Environment, string Trainer, int Rows, int Threads, int Trees, int Leaves, int Repeats, string OutDir);

public sealed record CalibrationResult(double Raw, double MlNetIsotonic, double OwnPav);

public sealed record RankedFeature(string Name, double Score);

public sealed record TrainerResult(
    string Trainer,
    int Threads,
    IReadOnlyList<double> TrainSeconds,
    double PrepSeconds,
    int TreesBuilt,
    double Auc,
    double AucOptimal,
    double AucLogistic,
    double LogLoss,
    double Brier,
    CalibrationResult Ece,
    double BatchRowsPerSecond,
    double SingleRowP50Ms,
    double SingleRowP95Ms,
    double ModelFileMb,
    double SaveLoadMaxDelta,
    double DeterminismMaxDelta,
    IReadOnlyList<RankedFeature> TopContributions,
    int PlantedInTopContributions,
    IReadOnlyList<RankedFeature> TopPermutationImportance,
    int PlantedInTopPermutation,
    double PeakMemoryMb);

public sealed record SpikeReport(EnvironmentInfo Environment, SpikeOptions Options, double GenerateSeconds, double PositiveRate, double LogisticAuc, IReadOnlyList<TrainerResult> Results, IReadOnlyList<string> Notes);

/// <summary>Trains, calibrates, explains, saves/loads and scores with the ML.NET gradient-boosted-tree trainers and records every measurement.</summary>
public sealed class Spike(SpikeOptions options, Action<string> log)
{
    private static readonly string[] Categoricals = ["Strategy", "Timeframe", "Side", "Style", "Regime", "Weekday"];
    private readonly MLContext _ml = new(seed: 1);

    public SpikeReport Run()
    {
        var notes = new List<string>();
        var clock = Stopwatch.StartNew();
        log($"Generating {options.Rows:N0} rows…");
        var rows = Generator.Generate(options.Rows, seed: 42);
        var generateSeconds = clock.Elapsed.TotalSeconds;

        // Time-ordered split: first 70% train, next 10% validation (early stopping + calibration), last 20% test.
        var trainEnd = (int)(rows.Length * 0.7);
        var valEnd = (int)(rows.Length * 0.8);
        var train = _ml.Data.LoadFromEnumerable(rows.AsSpan(0, trainEnd).ToArray());
        var validation = _ml.Data.LoadFromEnumerable(rows.AsSpan(trainEnd, valEnd - trainEnd).ToArray());
        var testRows = rows.AsSpan(valEnd).ToArray();
        var test = _ml.Data.LoadFromEnumerable(testRows);

        clock.Restart();
        var prep = BuildPreparation().Fit(train);
        var trainT = _ml.Data.Cache(prep.Transform(train));
        var valT = _ml.Data.Cache(prep.Transform(validation));
        var testT = _ml.Data.Cache(prep.Transform(test));
        var prepSeconds = clock.Elapsed.TotalSeconds;
        log($"Prepared features in {prepSeconds:F1}s");

        var testLabels = testRows.Select(r => r.Label).ToArray();
        var optimalAuc = Metrics.Auc([.. testRows.Select(r => (double)r.TrueProbability)], testLabels);
        var logisticAuc = LogisticBaseline(trainT, testT, testLabels);
        log($"Optimal AUC {optimalAuc:F4}, logistic baseline {logisticAuc:F4}");

        var results = new List<TrainerResult>();
        foreach (var trainer in options.Trainer == "both" ? new[] { "lightgbm", "fasttree" } : [options.Trainer])
        {
            results.Add(RunTrainer(trainer, prep, train, trainT, valT, testT, testRows, testLabels, optimalAuc, logisticAuc));
        }

        return new SpikeReport(EnvironmentInfo.Capture(options.Environment), options, generateSeconds, rows.Average(r => r.Label ? 1.0 : 0.0), logisticAuc, results, notes);
    }

    private EstimatorChain<ColumnConcatenatingTransformer> BuildPreparation()
    {
        var hot = Categoricals.Select(c => new InputOutputColumnPair(c + "Hot", c)).ToArray();
        return _ml.Transforms.Categorical.OneHotEncoding(hot)
            .Append(_ml.Transforms.Concatenate("Features", ["Numeric", .. Categoricals.Select(c => c + "Hot")]));
    }

    private double LogisticBaseline(IDataView trainT, IDataView testT, bool[] testLabels)
    {
        var normalise = _ml.Transforms.NormalizeMeanVariance("Features").Fit(trainT);
        var model = _ml.BinaryClassification.Trainers.SdcaLogisticRegression(labelColumnName: "Label", featureColumnName: "Features", maximumNumberOfIterations: 50)
            .Fit(normalise.Transform(trainT));
        var scored = model.Transform(normalise.Transform(testT));
        return Metrics.Auc([.. scored.GetColumn<float>("Score").Select(s => (double)s)], testLabels);
    }

    private TrainerResult RunTrainer(string name, ITransformer prep, IDataView train, IDataView trainT, IDataView valT, IDataView testT, Row[] testRows, bool[] testLabels, double optimalAuc, double logisticAuc)
    {
        log($"--- {name}: {options.Threads} threads, {options.Trees} trees, {options.Leaves} leaves ---");
        var times = new List<double>();
        ITransformer? model = null;
        for (var r = 0; r < options.Repeats; r++)
        {
            var watch = Stopwatch.StartNew();
            model = Train(name, trainT, valT, seed: 1);
            times.Add(watch.Elapsed.TotalSeconds);
            log($"  train #{r + 1}: {times[^1]:F1}s");
        }

        var scored = model!.Transform(testT);
        var rawScores = scored.GetColumn<float>("Score").Select(s => (double)s).ToArray();
        var platt = scored.GetColumn<float>("Probability").Select(s => (double)s).ToArray();
        var auc = Metrics.Auc(rawScores, testLabels);
        var calibration = Calibrate(model, valT, testT, platt, testLabels, rawScores);

        var chain = prep.Append(model);
        var (batchRps, p50, p95) = ScoringSpeed(chain, testRows);
        var (fileMb, saveLoadDelta) = SaveLoad(chain, train, testRows);
        var determinism = Determinism(name, trainT, valT);
        var (contrib, plantedContrib) = Contributions(model, testT);
        var (pfi, plantedPfi) = Importance(model, testT);
        var peak = Process.GetCurrentProcess().PeakWorkingSet64 / 1048576.0;
        return new TrainerResult(name, options.Threads, times, 0, options.Trees, auc, optimalAuc, logisticAuc, Metrics.LogLoss(platt, testLabels), Metrics.Brier(platt, testLabels), calibration, batchRps, p50, p95, fileMb, saveLoadDelta, determinism, contrib, plantedContrib, pfi, plantedPfi, peak);
    }

    private ITransformer Train(string name, IDataView trainT, IDataView valT, int seed) => name switch
    {
        "lightgbm" => _ml.BinaryClassification.Trainers.LightGbm(new LightGbmBinaryTrainer.Options
        {
            LabelColumnName = "Label",
            FeatureColumnName = "Features",
            NumberOfLeaves = options.Leaves,
            NumberOfIterations = options.Trees,
            LearningRate = 0.1,
            MinimumExampleCountPerLeaf = 100,
            NumberOfThreads = options.Threads,
            Seed = seed,
        }).Fit(trainT, valT),
        "fasttree" => _ml.BinaryClassification.Trainers.FastTree(new FastTreeBinaryTrainer.Options
        {
            LabelColumnName = "Label",
            FeatureColumnName = "Features",
            NumberOfLeaves = options.Leaves,
            NumberOfTrees = options.Trees,
            LearningRate = 0.1,
            MinimumExampleCountPerLeaf = 100,
            NumberOfThreads = options.Threads,
            Seed = seed,
        }).Fit(trainT, valT),
        _ => throw new ArgumentException($"Unknown trainer '{name}'."),
    };

    private CalibrationResult Calibrate(ITransformer model, IDataView valT, IDataView testT, double[] plattTest, bool[] testLabels, double[] rawTest)
    {
        var valScored = _ml.Transforms.DropColumns("Probability", "PredictedLabel").Fit(model.Transform(valT)).Transform(model.Transform(valT));
        var testScored = _ml.Transforms.DropColumns("Probability", "PredictedLabel").Fit(model.Transform(testT)).Transform(model.Transform(testT));
        var isotonic = _ml.BinaryClassification.Calibrators.Isotonic("Label", "Score").Fit(valScored);
        var isoProbabilities = isotonic.Transform(testScored).GetColumn<float>("Probability").Select(p => (double)p).ToArray();

        var valRaw = valScored.GetColumn<float>("Score").Select(s => (double)s).ToArray();
        var valLabels = valScored.GetColumn<bool>("Label").ToArray();
        var pav = Pav.Fit(valRaw, valLabels);
        var ownProbabilities = rawTest.Select(pav.Apply).ToArray();
        return new CalibrationResult(Metrics.Ece(plattTest, testLabels), Metrics.Ece(isoProbabilities, testLabels), Metrics.Ece(ownProbabilities, testLabels));
    }

    private (double BatchRowsPerSecond, double P50Ms, double P95Ms) ScoringSpeed(TransformerChain<ITransformer> chain, Row[] testRows)
    {
        var data = _ml.Data.LoadFromEnumerable(testRows);
        var rates = new List<double>();
        for (var i = 0; i < 3; i++)
        {
            var watch = Stopwatch.StartNew();
            var count = _ml.Data.CreateEnumerable<Scored>(chain.Transform(data), reuseRowObject: true).Count();
            rates.Add(count / watch.Elapsed.TotalSeconds);
        }

        using var engine = _ml.Model.CreatePredictionEngine<Row, Scored>(chain);
        var latencies = new List<double>();
        for (var i = 0; i < Math.Min(20000, testRows.Length); i++)
        {
            var start = Stopwatch.GetTimestamp();
            engine.Predict(testRows[i]);
            latencies.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }

        return (Metrics.Median(rates), Metrics.Percentile(latencies, 0.5), Metrics.Percentile(latencies, 0.95));
    }

    private (double FileMb, double MaxDelta) SaveLoad(TransformerChain<ITransformer> chain, IDataView train, Row[] testRows)
    {
        Directory.CreateDirectory(options.OutDir);
        var path = Path.Combine(options.OutDir, $"model-{options.Environment}-{Guid.NewGuid():N}.zip");
        _ml.Model.Save(chain, train.Schema, path);
        var size = new FileInfo(path).Length / 1048576.0;
        var loaded = _ml.Model.Load(path, out _);
        var sample = testRows.Take(5000).ToArray();
        var original = _ml.Data.CreateEnumerable<Scored>(chain.Transform(_ml.Data.LoadFromEnumerable(sample)), reuseRowObject: false).ToArray();
        using var engine = _ml.Model.CreatePredictionEngine<Row, Scored>(loaded);
        var max = 0.0;
        for (var i = 0; i < sample.Length; i++)
        {
            max = Math.Max(max, Math.Abs(engine.Predict(sample[i]).Probability - original[i].Probability));
        }

        File.Delete(path);
        return (size, max);
    }

    private double Determinism(string name, IDataView trainT, IDataView valT)
    {
        var small = _ml.Data.TakeRows(trainT, 100_000);
        var a = Train(name, small, valT, seed: 7).Transform(valT).GetColumn<float>("Score").Take(10_000).ToArray();
        var b = Train(name, small, valT, seed: 7).Transform(valT).GetColumn<float>("Score").Take(10_000).ToArray();
        return a.Zip(b, (x, y) => Math.Abs((double)x - y)).Max();
    }

    private (IReadOnlyList<RankedFeature> Top, int Planted) Contributions(ITransformer model, IDataView testT)
    {
        var names = SlotNames(testT);
        var sample = _ml.Data.TakeRows(testT, 2000);
        var contributionTransformer = Contribution(model, sample);
        var withContributions = contributionTransformer.Transform(sample);
        var sums = new double[names.Length];
        foreach (var vector in withContributions.GetColumn<float[]>("FeatureContributions"))
        {
            for (var i = 0; i < vector.Length && i < sums.Length; i++)
            {
                sums[i] += Math.Abs(vector[i]);
            }
        }

        return Rank(names, sums);
    }

    private FeatureContributionCalculatingTransformer Contribution(ITransformer model, IDataView data) => model switch
    {
        BinaryPredictionTransformer<CalibratedModelParametersBase<LightGbmBinaryModelParameters, PlattCalibrator>> lgbm => _ml.Transforms
            .CalculateFeatureContribution(lgbm, numberOfPositiveContributions: 10, numberOfNegativeContributions: 10, normalize: false).Fit(data),
        BinaryPredictionTransformer<CalibratedModelParametersBase<FastTreeBinaryModelParameters, PlattCalibrator>> fast => _ml.Transforms
            .CalculateFeatureContribution(fast, numberOfPositiveContributions: 10, numberOfNegativeContributions: 10, normalize: false).Fit(data),
        _ => throw new NotSupportedException($"No feature-contribution support wired for {model.GetType().Name}."),
    };

    private (IReadOnlyList<RankedFeature> Top, int Planted) Importance(ITransformer model, IDataView testT)
    {
        var names = SlotNames(testT);
        var sample = _ml.Data.TakeRows(testT, 20_000);
        var stats = model switch
        {
            BinaryPredictionTransformer<CalibratedModelParametersBase<LightGbmBinaryModelParameters, PlattCalibrator>> lgbm => _ml.BinaryClassification.PermutationFeatureImportance(lgbm, sample, permutationCount: 1),
            BinaryPredictionTransformer<CalibratedModelParametersBase<FastTreeBinaryModelParameters, PlattCalibrator>> fast => _ml.BinaryClassification.PermutationFeatureImportance(fast, sample, permutationCount: 1),
            _ => throw new NotSupportedException(),
        };
        var drops = new double[names.Length];
        for (var i = 0; i < names.Length && i < stats.Length; i++)
        {
            drops[i] = -stats[i].AreaUnderRocCurve.Mean; // a feature matters if permuting it lowers AUC (negative delta)
        }

        return Rank(names, drops);
    }

    private static (IReadOnlyList<RankedFeature> Top, int Planted) Rank(string[] names, double[] scores)
    {
        var top = Enumerable.Range(0, names.Length).OrderByDescending(i => scores[i]).Take(8).Select(i => new RankedFeature(names[i], scores[i])).ToList();
        var planted = top.Take(6).Count(f => Generator.PlantedFeatures.Contains(f.Name, StringComparer.Ordinal));
        return (top, planted);
    }

    private static string[] SlotNames(IDataView data)
    {
        VBuffer<ReadOnlyMemory<char>> names = default;
        data.Schema["Features"].GetSlotNames(ref names);
        return [.. names.DenseValues().Select(n => n.ToString())];
    }
}
