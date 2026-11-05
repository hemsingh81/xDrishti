using System.Globalization;
using XDrishti.Domain.Confidence;

namespace XDrishti.Domain.Learning;

/// <summary>The steps by which the harness collects the complexity evidence (CF-10 part 10b); the maths is in <see cref="EventsPerVariable"/>, <see cref="ComplexityLadder"/>, <see cref="Psi"/> and <see cref="FeatureStability"/>. Pure.</summary>
internal static class ComplexityEvidence
{
    /// <summary>Why a candidate of the harness is not on the trees rung of the spec (D2) whatever its numbers.</summary>
    public const string TreesNotRung4 = "the trainer has 8 leaves per tree and no monotone constraints, which the trees rung of the ladder requires (monotone, depth at most 3)";

    /// <summary>The direction of every feature in one fold, from the contributions the scorer gives for the test rows.</summary>
    public static int[] FoldSigns(IExplainableScorer scorer, ValidationRow[] test, int width, int minRows)
    {
        var contributions = new double[test.Length][];
        for (var k = 0; k < test.Length; k++)
        {
            contributions[k] = new double[width];
            foreach (var c in scorer.Explain(test[k], width))
            {
                if (c.Index >= 0 && c.Index < width)
                {
                    contributions[k][c.Index] = c.Value;
                }
            }
        }

        var signs = new int[width];
        for (var j = 0; j < width; j++)
        {
            var j1 = j;
            signs[j] = FeatureStability.Direction([.. test.Select(r => r.Features[j1])], [.. contributions.Select(row => row[j1])], minRows);
        }

        return signs;
    }

    /// <summary>The population stability index of every feature, the fold's test rows against its train rows (null where it cannot be read).</summary>
    public static double?[] FoldPsi(ValidationRow[] train, ValidationRow[] test, int width, LadderOptions options)
    {
        var psi = new double?[width];
        for (var j = 0; j < width; j++)
        {
            var j1 = j;
            psi[j] = Psi.Compute([.. train.Select(r => r.Features[j1])], [.. test.Select(r => r.Features[j1])], options.PsiBins, options.MinRows);
        }

        return psi;
    }

    /// <summary>
    /// The paired comparisons of the ladder (CF-10 part 10c-1): the logistic baseline against the cell rate, each candidate against the logistic baseline, on the pooled out-of-sample rows of the used folds. The gain of a row is the log loss of the rung below minus the log loss of the scorer;
    /// the sign test reads the same gain per fold. <paramref name="scores"/> holds one list per scorer, baselines first (base rate, cell rate, logistic), then the candidates.
    /// </summary>
    public static List<RungComparison> Pair(
        IReadOnlyList<string> names,
        List<double>[] scores,
        List<int> labels,
        List<DateOnly> days,
        IReadOnlyList<FoldResult> results,
        int baselineCount,
        PairedBootstrapOptions options,
        double passAt)
    {
        var span = string.Create(CultureInfo.InvariantCulture, $"{days.Min():yyyy-MM-dd}|{days.Max():yyyy-MM-dd}|{days.Count}");
        var pairs = new List<RungComparison>(names.Count - baselineCount + 1);
        var logistic = baselineCount - 1; // the baselines are base rate, cell rate, logistic, in that order
        var cellRate = baselineCount - 2;
        for (var s = logistic; s < names.Count; s++)
        {
            var below = s == logistic ? cellRate : logistic;
            var gains = new double[labels.Count];
            for (var i = 0; i < gains.Length; i++)
            {
                gains[i] = MetricsCalculator.RowLogLoss(scores[below][i], labels[i]) - MetricsCalculator.RowLogLoss(scores[s][i], labels[i]);
            }

            var sign = FoldSignTest.Assess([.. results.Select(r => r.Scorers[below].Metrics.LogLoss - r.Scorers[s].Metrics.LogLoss)]);
            var bootstrap = PairedDayBootstrap.Assess(gains, days, options.Draws, options.Level, $"{names[s]}|{names[below]}|{span}", options.MinDays);
            pairs.Add(bootstrap.IsSuccess
                ? new RungComparison(names[s], names[below], bootstrap.Value, null, bootstrap.Value.Reading(passAt, options.PromisingFrom), sign)
                : new RungComparison(names[s], names[below], null, bootstrap.Error.Code, PairedReading.Below, sign));
        }

        return pairs;
    }

    /// <summary>How many independent labels the training rows are worth (the sum of their average uniqueness over the minute bars they span), or null when a span cannot be read.</summary>
    public static double? EffectiveRows(ValidationRow[] train)
    {
        // shortcut: spans count calendar minutes, not trading bars; switch to session bars when uniqueness weights feed training
        var uniqueness = UniquenessWeights.Compute([.. train.Select(r => (r.DecisionEpochSeconds / HoldOut.BarSeconds, r.ResolvedEpochSeconds / HoldOut.BarSeconds))]);
        return uniqueness.IsSuccess ? uniqueness.Value.EffectiveCount : null;
    }

    /// <summary>Puts the pieces together. <paramref name="signs"/> holds, by candidate, one array per used fold (null when the scorer of that fold could not explain); <paramref name="psi"/> one entry per used fold with its index. <paramref name="paired"/> holds the paired comparisons (null or empty when the paired bootstrap is off: the probability of beating the rung below is then not available and the rung reads as not earned).</summary>
    public static ComplexityReport Build(
        IReadOnlyList<string> candidates,
        int width,
        ValidationRow[] lastTrain,
        List<int[]?>[] signs,
        List<(int Fold, double?[] Psi)> psi,
        LadderOptions options,
        IReadOnlyList<RungComparison>? paired = null,
        double? effectiveRows = null,
        string? pairedSettings = null)
    {
        var wins = lastTrain.Count(r => r.Label == 1);
        var losses = lastTrain.Length - wins;
        var events = EventsPerVariable.Events(wins, losses);
        double? ProbabilityOf(string scorer) => paired?.FirstOrDefault(p => p.Scorer == scorer)?.Bootstrap?.ProbabilityBetter;
        var ladder = new List<LadderAssessment> { ComplexityLadder.Assess(LogisticScorer.ScorerName, LadderRung.Logistic, events, width, ProbabilityOf(LogisticScorer.ScorerName), options) };
        // shortcut: D2's rung 4 is gradient-boosted trees with monotone constraints and depth at most 3; the trainer has 8 leaves per tree and no monotone constraint, so a candidate can never read as earned here until the trainer meets D2 (then this reason goes).
        ladder.AddRange(candidates.Select(name => ComplexityLadder.Assess(name, LadderRung.Trees, events, width, ProbabilityOf(name), options, TreesNotRung4)));

        var shifts = new List<FeatureShift>(width);
        for (var j = 0; j < width; j++)
        {
            double? worst = null;
            int? fold = null;
            for (var f = 0; f < psi.Count; f++)
            {
                if (psi[f].Psi[j] is { } v && (worst is null || v > worst))
                {
                    worst = v;
                    fold = psi[f].Fold;
                }
            }

            shifts.Add(new FeatureShift(j, worst, fold, worst is { } w ? Psi.Level(w, options) : null));
        }

        var stability = new List<CandidateStability>(candidates.Count);
        for (var k = 0; k < candidates.Count; k++)
        {
            var folds = signs[k];
            var explained = folds.Any(s => s is not null);
            var rows = new List<FeatureStabilityRow>(width);
            for (var j = 0; j < width; j++)
            {
                var j1 = j;
                rows.Add(FeatureStability.Judge(j, [.. folds.Select(s => s?[j1] ?? 0)], options.MaxFlipShare));
            }

            stability.Add(new CandidateStability(candidates[k], explained, rows.AsReadOnly()));
        }

        return new ComplexityReport(lastTrain.Length, wins, losses, psi.Count, EventsPerVariable.Cap(events, options.Epv, options.MaxLogisticFeatures), ladder.AsReadOnly(), shifts.AsReadOnly(), stability.AsReadOnly(), pairedSettings is null ? options.KeyText() : options.KeyText() + "; paired " + pairedSettings, options.PsiBins, effectiveRows, paired ?? []);
    }
}
