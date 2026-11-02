using System.Globalization;
using XDrishti.Domain.Common;

namespace XDrishti.Domain.Learning.Extensions;

/// <summary>One used fold with the out-of-fold scores of both models, in the order of <see cref="CalendarFold.Test"/>.</summary>
internal sealed record ScoredFold(CalendarFold Fold, double[] A, double[] B);

/// <summary>
/// The paired fitting shared by the comparison and the hold-out check (P13-05). Rows carry model B's width; model A sees <c>Features[..baseWidth]</c> of the same rows, so rows, labels, R, dates, folds, train sets and seeds are the same by construction, and one recipe is fitted twice per fold.
/// </summary>
internal static class ExtensionScoring
{
    /// <summary>The A view of the rows: the same rows with only the first <paramref name="baseWidth"/> features.</summary>
    public static Dictionary<string, ValidationRow> ViewA(IReadOnlyList<ValidationRow> rows, int baseWidth)
    {
        var view = new Dictionary<string, ValidationRow>(rows.Count, StringComparer.Ordinal);
        foreach (var row in rows)
        {
            view.Add(row.Key, row with { Features = [.. row.Features.AsSpan(0, baseWidth)] });
        }

        return view;
    }

    /// <summary>Fits A and B on each usable fold and scores its test rows.</summary>
    public static Result<List<ScoredFold>> Score(IEnumerable<CalendarFold> usable, IReadOnlyDictionary<string, ValidationRow> viewA, IScorerFactory recipe)
    {
        var scored = new List<ScoredFold>();
        foreach (var fold in usable)
        {
            var scorerA = recipe.Fit([.. fold.Train.Select(r => viewA[r.Key])]);
            var scorerB = recipe.Fit(fold.Train);
            var a = new double[fold.Test.Count];
            var b = new double[fold.Test.Count];
            for (var i = 0; i < fold.Test.Count; i++)
            {
                var row = fold.Test[i];
                a[i] = scorerA.Score(viewA[row.Key]);
                b[i] = scorerB.Score(row);
                if (!IsProbability(a[i]) || !IsProbability(b[i]))
                {
                    return Result.Failure<List<ScoredFold>>(Error.Validation("validation.bad_probability", string.Create(CultureInfo.InvariantCulture, $"{recipe.Name} gave {a[i]} or {b[i]} for {row.Key}; a probability is finite and in [0, 1].")));
                }
            }

            scored.Add(new ScoredFold(fold, a, b));
        }

        return Result.Success(scored);
    }

    /// <summary>The threshold of model <paramref name="model"/> (0 for A, 1 for B) for fold <paramref name="index"/>, from the out-of-fold scores of earlier folds that belong to its train set; null when there are too few.</summary>
    public static double? ThresholdFor(IReadOnlyList<ScoredFold> folds, int index, int model, ExtensionComparisonOptions options)
    {
        var trainKeys = new HashSet<string>(folds[index].Fold.Train.Select(r => r.Key), StringComparer.Ordinal);
        var pool = new List<(double Score, double R)>();
        for (var i = 0; i < index; i++)
        {
            var fold = folds[i];
            var scores = model == 0 ? fold.A : fold.B;
            for (var k = 0; k < fold.Fold.Test.Count; k++)
            {
                if (trainKeys.Contains(fold.Fold.Test[k].Key))
                {
                    pool.Add((scores[k], fold.Fold.Test[k].R));
                }
            }
        }

        return pool.Count < options.Take.MinThresholdRows ? null : TakeThreshold.Choose(pool, options.Take.ThresholdStep, options.Take.MinKeptShare).Threshold;
    }

    /// <summary>The threshold from every out-of-fold score of every fold, for the hold-out reading.</summary>
    public static double? ThresholdFromAll(IReadOnlyList<ScoredFold> folds, int model, ExtensionComparisonOptions options)
    {
        var pool = new List<(double Score, double R)>();
        foreach (var fold in folds)
        {
            var scores = model == 0 ? fold.A : fold.B;
            for (var k = 0; k < fold.Fold.Test.Count; k++)
            {
                pool.Add((scores[k], fold.Fold.Test[k].R));
            }
        }

        return pool.Count < options.Take.MinThresholdRows ? null : TakeThreshold.Choose(pool, options.Take.ThresholdStep, options.Take.MinKeptShare).Threshold;
    }

    /// <summary>The checks on the rows that both entry points make: unique keys, labels 0 or 1, finite R, one width, none in the hold-out.</summary>
    public static Error? CheckRows(IReadOnlyList<ValidationRow> rows, int expectedWidth, DateOnly? holdOutStart)
    {
        if (rows.Count == 0)
        {
            return Error.Validation("extensions.no_rows", "There are no rows.");
        }

        var keys = new HashSet<string>(rows.Count, StringComparer.Ordinal);
        foreach (var r in rows)
        {
            if (!keys.Add(r.Key))
            {
                return Error.Conflict("extensions.duplicate_key", "Two rows have the key " + r.Key + ".");
            }

            if (r.Label is not (0 or 1) || !double.IsFinite(r.R))
            {
                return Error.Validation("extensions.bad_row", "Row " + r.Key + " needs a label of 0 or 1 and a finite R.");
            }

            if (r.Features.Length != expectedWidth)
            {
                return Error.Validation("extensions.feature_width", string.Create(CultureInfo.InvariantCulture, $"Row {r.Key} has {r.Features.Length} features, {expectedWidth} expected (the base width plus the extension names)."));
            }

            if (holdOutStart is { } h && r.TradeDate >= h)
            {
                return Error.Validation("extensions.holdout_touched", "Row " + r.Key + " is in the hold-out (from " + h.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "); development may not read it.");
            }
        }

        return null;
    }

    private static bool IsProbability(double p) => double.IsFinite(p) && p is >= 0 and <= 1;
}
