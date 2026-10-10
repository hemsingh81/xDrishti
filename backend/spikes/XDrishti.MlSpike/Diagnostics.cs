using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Trainers.LightGbm;

namespace XDrishti.MlSpike;

/// <summary>LightGBM option variants on the same data: shows which settings change what the trees can learn.</summary>
public static class Diagnostics
{
    public static IEnumerable<string> LightGbmVariants(int rows)
    {
        var ml = new MLContext(seed: 1);
        var data = Generator.Generate(rows, 42);
        var split = (int)(rows * 0.8);
        var train = ml.Data.LoadFromEnumerable(data.AsSpan(0, split).ToArray());
        var test = ml.Data.LoadFromEnumerable(data.AsSpan(split).ToArray());
        var labels = data.AsSpan(split).ToArray().Select(r => r.Label).ToArray();
        var cats = new[] { "Strategy", "Timeframe", "Side", "Style", "Regime", "Weekday" };

        var numericOnly = ml.Transforms.Concatenate("Features", "Numeric").Fit(train);
        var withHot = ml.Transforms.Categorical.OneHotEncoding(cats.Select(c => new InputOutputColumnPair(c + "Hot", c)).ToArray())
            .Append(ml.Transforms.Concatenate("Features", ["Numeric", .. cats.Select(c => c + "Hot")])).Fit(train);

        var variants = new (string Name, ITransformer Prep, Action<LightGbmBinaryTrainer.Options> Tune)[]
        {
            ("numeric only, Silent=false (shows LightGBM log)", numericOnly, o => o.Silent = false),
            ("numeric only, 1 thread", numericOnly, o => o.NumberOfThreads = 1),
            ("numeric only, HandleMissingValue=false", numericOnly, o => o.HandleMissingValue = false),
            ("numeric features only, defaults", numericOnly, _ => { }),
            ("numeric + one-hot, defaults", withHot, _ => { }),
            ("numeric + one-hot, 20 min per leaf", withHot, o => o.MinimumExampleCountPerLeaf = 20),
            ("numeric + one-hot, UseCategoricalSplit", withHot, o => o.UseCategoricalSplit = true),
            ("numeric + one-hot, 300 iterations lr 0.1", withHot, o => o.NumberOfIterations = 300),
            ("numeric + one-hot, MaximumBinCountPerFeature 1023", withHot, o => o.MaximumBinCountPerFeature = 1023),
        };
        foreach (var (name, prep, tune) in variants)
        {
            var options = new LightGbmBinaryTrainer.Options { LabelColumnName = "Label", FeatureColumnName = "Features", NumberOfLeaves = 31, NumberOfIterations = 60, LearningRate = 0.1, MinimumExampleCountPerLeaf = 100, NumberOfThreads = 8, Seed = 1 };
            tune(options);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var model = ml.BinaryClassification.Trainers.LightGbm(options).Fit(prep.Transform(train));
            var seconds = watch.Elapsed.TotalSeconds;
            var scores = model.Transform(prep.Transform(test)).GetColumn<float>("Score").Select(s => (double)s).ToArray();
            yield return $"{name,-50} AUC {Metrics.Auc(scores, labels):F4}  train {seconds:F1}s";
        }
    }
}
