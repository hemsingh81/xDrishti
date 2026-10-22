using System.Globalization;
using XDrishti.Domain.Common;
using XDrishti.Domain.Research;

namespace XDrishti.Domain.Learning;

/// <summary>
/// The validation harness (P6-05, ADR 0067): purged expanding walk-forward folds over labelled rows, three baselines (base rate, cell rate, logistic) and any number of candidate scorers, per-fold and pooled out-of-sample metrics, and the verdict of
/// <see cref="EligibilityRule"/>. Pure: no clock, no random, no I/O. The fold windows are those of the exit choice (<see cref="ExitSelector.Windows"/>, ADR 0054). A scorer sees only the train rows of a fold.
/// </summary>
public static class WalkForwardValidator
{
    /// <summary>Compares the candidates with the baselines.</summary>
    /// <param name="rows">Development rows (not the hold-out), unique keys, labels 0 or 1.</param>
    /// <param name="candidates">The scorers to judge; names unique and not a baseline's.</param>
    /// <param name="options">The options.</param>
    public static Result<ValidationReport> Compare(IReadOnlyList<ValidationRow> rows, IReadOnlyList<IScorerFactory> candidates, ValidationOptions options)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(options);
        var problems = options.Problems();
        if (problems.Count > 0)
        {
            return Fail("validation.options", string.Join("; ", problems));
        }

        var baselines = new IScorerFactory[] { new BaseRateScorer(), new CellRateScorer(), new LogisticScorer() };
        var scorers = baselines.Concat(candidates).ToArray();
        if (scorers.Select(s => s.Name).Distinct(StringComparer.Ordinal).Count() != scorers.Length)
        {
            return Fail("validation.scorer_names", "Scorer names must be unique and must not reuse a baseline name.");
        }

        var check = CheckRows(rows, options);
        if (check is not null)
        {
            return Result.Failure<ValidationReport>(check);
        }

        var ordered = rows.OrderBy(r => r.TradeDate).ThenBy(r => r.DecisionEpochSeconds).ThenBy(r => r.Key, StringComparer.Ordinal).ToArray();
        var from = ordered[0].TradeDate;
        var to = ordered[^1].TradeDate;
        var (windows, dropped) = ExitSelector.Windows(from, to, new SelectionOptions(options.MinTrainMonths, options.TestMonths));

        var folds = new List<ValidationFold>();
        var results = new List<FoldResult>();
        var pooledScores = scorers.Select(_ => new List<double>()).ToArray();
        var pooledLabels = new List<int>();
        var notes = new SortedSet<string>(StringComparer.Ordinal);
        var lastTrain = 0;
        var comparisons = candidates.Select(_ => new List<BaselineComparison>()).ToArray();
        for (var i = 0; i < windows.Count; i++)
        {
            var (start, end) = windows[i];
            var test = ordered.Where(r => r.TradeDate >= start && r.TradeDate <= end).ToArray();
            var before = ordered.Where(r => r.TradeDate < start).ToArray();
            var fold = Cut(i + 1, start, end, before, test, options);
            folds.Add(fold.Fold);
            if (!fold.Fold.Usable)
            {
                continue;
            }

            var labels = test.Select(r => r.Label).ToArray();
            var metrics = new List<ScorerFoldResult>();
            for (var s = 0; s < scorers.Length; s++)
            {
                var scorer = scorers[s].Fit(fold.Train);
                if (scorer is IFitNote { Note: { } note })
                {
                    notes.Add(note);
                }

                var scores = new double[test.Length];
                for (var k = 0; k < test.Length; k++)
                {
                    scores[k] = scorer.Score(test[k]);
                    if (!double.IsFinite(scores[k]) || scores[k] is < 0 or > 1)
                    {
                        return Fail("validation.bad_probability", string.Create(CultureInfo.InvariantCulture, $"{scorers[s].Name} gave {scores[k]} for {test[k].Key}; a probability is finite and in [0, 1]."));
                    }
                }

                pooledScores[s].AddRange(scores);
                metrics.Add(new ScorerFoldResult(scorers[s].Name, MetricsCalculator.Compute(scores, labels, options.CalibrationBins)));
            }

            pooledLabels.AddRange(labels);
            lastTrain = fold.Train.Length;
            for (var k = 0; k < candidates.Count; k++)
            {
                comparisons[k].Add(new BaselineComparison(metrics[baselines.Length + k].Metrics, metrics[1].Metrics, metrics[2].Metrics));
            }

            results.Add(new FoldResult(fold.Fold, new AlwaysTakeSummary(test.Length, labels.Count(l => l == 1) / (double)test.Length, test.Average(r => r.R)), metrics));
        }

        if (results.Count == 0)
        {
            var c = CultureInfo.InvariantCulture;
            return Fail("validation.no_folds", string.Create(c, $"No usable fold in {from:yyyy-MM-dd} to {to:yyyy-MM-dd}: {windows.Count} windows after {options.MinTrainMonths} months of training, {dropped} short final window dropped, {folds.Count(f => !f.Usable)} skipped."));
        }

        var pooled = scorers.Select((s, i) => new ScorerFoldResult(s.Name, MetricsCalculator.Compute(pooledScores[i], pooledLabels, options.CalibrationBins))).ToArray();
        var verdicts = new List<CandidateVerdict>();
        for (var k = 0; k < candidates.Count; k++)
        {
            var p = new BaselineComparison(pooled[baselines.Length + k].Metrics, pooled[1].Metrics, pooled[2].Metrics);
            verdicts.Add(EligibilityRule.Judge(candidates[k].Name, comparisons[k], p, lastTrain, options));
        }

        return Result.Success(new ValidationReport(options, ordered.Length, from, to, folds, results, pooled, verdicts, LimitsOf(options, ordered.Length, from, to, results.Count, notes)));
    }

    private static (ValidationFold Fold, ValidationRow[] Train) Cut(int index, DateOnly start, DateOnly end, ValidationRow[] before, ValidationRow[] test, ValidationOptions options)
    {
        if (test.Length == 0)
        {
            return (new ValidationFold(index, start, end, 0, 0, 0, 0, "no rows in the test window"), []);
        }

        var firstDecision = test.Min(r => r.DecisionEpochSeconds);
        var embargoFrom = start.AddDays(-options.EmbargoDays);
        var embargoed = before.Where(r => r.TradeDate >= embargoFrom).ToArray();
        var kept = before.Where(r => r.TradeDate < embargoFrom).ToArray();
        var train = kept.Where(r => r.ResolvedEpochSeconds + HoldOut.BarSeconds < firstDecision).ToArray();
        var purged = kept.Length - train.Length;
        var reason = train.Length < options.MinTrainTrades ? string.Create(CultureInfo.InvariantCulture, $"only {train.Length} train rows, {options.MinTrainTrades} needed") : null;
        return (new ValidationFold(index, start, end, train.Length, test.Length, purged, embargoed.Length, reason), train);
    }

    private static Error? CheckRows(IReadOnlyList<ValidationRow> rows, ValidationOptions options)
    {
        if (rows.Count == 0)
        {
            return Error.Validation("validation.no_rows", "There are no rows.");
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        var width = rows[0].Features.IsDefault ? 0 : rows[0].Features.Length;
        foreach (var r in rows)
        {
            if (!keys.Add(r.Key))
            {
                return Error.Conflict("validation.duplicate_key", "Two rows have the key " + r.Key + ".");
            }

            if (r.Label is not (0 or 1) || !double.IsFinite(r.R))
            {
                return Error.Validation("validation.bad_row", "Row " + r.Key + " needs a label of 0 or 1 and a finite R.");
            }

            if ((r.Features.IsDefault ? 0 : r.Features.Length) != width)
            {
                return Error.Validation("validation.feature_width", "Row " + r.Key + " has a different number of features from the first row.");
            }

            if (options.HoldOutStart is { } h && r.TradeDate >= h)
            {
                return Error.Validation("validation.holdout_touched", "Row " + r.Key + " is in the hold-out (from " + h.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "); development may not read it.");
            }
        }

        return null;
    }

    private static List<string> LimitsOf(ValidationOptions o, int rows, DateOnly from, DateOnly to, int folds, IEnumerable<string> notes)
    {
        var c = CultureInfo.InvariantCulture;
        var limits = new List<string>
        {
            string.Create(c, $"{rows} trades, {from:yyyy-MM-dd} to {to:yyyy-MM-dd}, {folds} out-of-sample folds of {o.TestMonths} months."),
            "Replayed history with modelled fills; the folds are out of sample for the model but the data have been seen (ISS-19).",
        };
        limits.Add(o.HoldOutStart is { } h
            ? string.Create(c, $"The hold-out from {h:yyyy-MM-dd} was not read.")
            : "No hold-out was reserved for this comparison.");
        if (folds < o.MinFoldsForVerdict)
        {
            limits.Add(string.Create(c, $"Fewer than {o.MinFoldsForVerdict} folds: no candidate can be called eligible, at best inconclusive."));
        }

        if (o.Trials > 1)
        {
            limits.Add(string.Create(c, $"{o.Trials} trials: margins widened by {Lab.MultipleTesting.Factor(o.Trials):F2}."));
        }

        limits.AddRange(notes);
        return limits;
    }

    private static Result<ValidationReport> Fail(string code, string message) => Result.Failure<ValidationReport>(Error.Validation(code, message));
}
