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

    /// <summary>Puts the pieces together. <paramref name="signs"/> holds, by candidate, one array per used fold (null when the scorer of that fold could not explain); <paramref name="psi"/> one entry per used fold with its index. The probability of beating the rung below is not computed here (the paired bootstrap is part 10c), so every rung above the base rate reads as not earned.</summary>
    public static ComplexityReport Build(
        IReadOnlyList<string> candidates,
        int width,
        ValidationRow[] lastTrain,
        List<int[]?>[] signs,
        List<(int Fold, double?[] Psi)> psi,
        LadderOptions options)
    {
        var wins = lastTrain.Count(r => r.Label == 1);
        var losses = lastTrain.Length - wins;
        var events = EventsPerVariable.Events(wins, losses);
        var ladder = new List<LadderAssessment> { ComplexityLadder.Assess(LogisticScorer.ScorerName, LadderRung.Logistic, events, width, null, options) };
        // shortcut: D2's rung 4 is gradient-boosted trees with monotone constraints and depth at most 3; the trainer has 8 leaves per tree and no monotone constraint, so a candidate can never read as earned here until the trainer meets D2 (then this reason goes).
        ladder.AddRange(candidates.Select(name => ComplexityLadder.Assess(name, LadderRung.Trees, events, width, null, options, TreesNotRung4)));

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

        return new ComplexityReport(lastTrain.Length, wins, losses, psi.Count, EventsPerVariable.Cap(events, options.Epv, options.MaxLogisticFeatures), ladder.AsReadOnly(), shifts.AsReadOnly(), stability.AsReadOnly(), options.KeyText(), options.PsiBins);
    }
}
